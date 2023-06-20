using System.Text.Json;
using System.Text.Json.Serialization;
using gspro_r10.OpenConnect;
using Microsoft.Extensions.Configuration;
using System.Media;
namespace gspro_r10
{
    public class ConnectionManager : IDisposable
    {
        private R10ConnectionServer? R10Server;
        private OpenConnectClient OpenConnectClient;
        private BluetoothConnection? BluetoothConnection { get; }
        internal HttpPuttingServer? PuttingConnection { get; }


        private bool ignoreVlaMisreads = false;
        private decimal minimumVLA = -1;
        private bool playSoundOnMisread = false;
        private string misreadAudioFile = "";

        internal IConfigurationRoot config { get; }
        public event ClubChangedEventHandler? ClubChanged;
        public delegate void ClubChangedEventHandler(object sender, ClubChangedEventArgs e);

        public class ClubChangedEventArgs : EventArgs
        {
            public Club Club { get; set; }
        }

        private JsonSerializerOptions serializerSettings = new JsonSerializerOptions()
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        private int shotNumber = 0;
        private bool disposedValue;

        public ConnectionManager(IConfigurationRoot configuration)
        {
            config = configuration;
            OpenConnectClient = new OpenConnectClient(this, configuration.GetSection("openConnect"));
            OpenConnectClient.ConnectAsync();

            if (bool.Parse(configuration.GetSection("r10E6Server")["enabled"] ?? "false"))
            {
                R10Server = new R10ConnectionServer(this, configuration.GetSection("r10E6Server"));
                R10Server.Start();
            }

            if (bool.Parse(configuration.GetSection("bluetooth")["enabled"] ?? "false"))
                BluetoothConnection = new BluetoothConnection(this, configuration.GetSection("bluetooth"));

            if (bool.Parse(configuration.GetSection("putting")["enabled"] ?? "false"))
            {
                PuttingConnection = new HttpPuttingServer(this, configuration.GetSection("putting"));
                PuttingConnection.Start();
            }

            if (bool.Parse(configuration.GetSection("bluetooth")["enabled"] ?? "false"))
            {
                PuttingConnection = new HttpPuttingServer(this, configuration.GetSection("putting"));
                PuttingConnection.Start();
            }

            bool.TryParse(configuration.GetSection("bluetooth")["ignoreVLAMisreads"], out ignoreVlaMisreads);
            decimal.TryParse(configuration.GetSection("bluetooth")["enabled"], out minimumVLA);
            bool.TryParse(configuration.GetSection("bluetooth")["playSoundOnMisread"], out playSoundOnMisread);
            misreadAudioFile = configuration.GetSection("bluetooth")["MisreadAudioFile"].ToString();
                        
        }

        internal void SendShot(OpenConnect.BallData? ballData, OpenConnect.ClubData? clubData)
        {
            string openConnectMessage = JsonSerializer.Serialize(OpenConnectApiMessage.CreateShotData(
            shotNumber++,
            ballData,
            clubData
            ), serializerSettings);


            if (this.ignoreVlaMisreads) 
            { 
                if (ballData != null && ballData.VLA > (double)minimumVLA)
                {
                    OpenConnectClient.SendAsync(openConnectMessage);
                }
                else
                {
                    OpenConnectLogger.LogGSPOutgoing(openConnectMessage);
                    if (playSoundOnMisread)
                    {
                        SoundPlayer homer = new SoundPlayer();
                        homer.SoundLocation = Environment.CurrentDirectory + "/" + misreadAudioFile;
                        homer.Play();
                    }
                }
            } 
            else
            {
                OpenConnectClient.SendAsync(openConnectMessage);
            }            
        }

        public void ClubUpdate(Club club)
        {
            Task.Run(() =>
            {
                ClubChanged?.Invoke(this, new ClubChangedEventArgs()
                {
                    Club = club
                });
            });

        }

        internal void SendLaunchMonitorReadyUpdate(bool deviceReady)
        {
            OpenConnectClient.SetDeviceReady(deviceReady);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    R10Server?.Dispose();
                    PuttingConnection?.Dispose();
                    BluetoothConnection?.Dispose();
                    OpenConnectClient?.DisconnectAndStop();
                    OpenConnectClient?.Dispose();
                }
                disposedValue = true;
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}