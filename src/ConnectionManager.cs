using System.Text.Json;
using System.Text.Json.Serialization;
using gspro_r10.OpenConnect;
using Microsoft.Extensions.Configuration;
using System.Media;
using System.Drawing.Imaging;
using System.Speech.Synthesis;
using static LaunchMonitor.Proto.Metrics.Types;

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
        private bool playSoundOnPracticeSwing = false;
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
            decimal.TryParse(configuration.GetSection("bluetooth")["minimumVLA"], out minimumVLA);
            bool.TryParse(configuration.GetSection("bluetooth")["playSoundOnMisread"], out playSoundOnMisread);
            bool.TryParse(configuration.GetSection("bluetooth")["playSoundOnPracticeSwing"], out playSoundOnPracticeSwing);
            misreadAudioFile = configuration.GetSection("bluetooth")["MisreadAudioFile"].ToString();
                        
        }

        internal void SendShot(ShotType? st, OpenConnect.BallData? ballData, OpenConnect.ClubData? clubData)
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
                    if (playSoundOnMisread && (st.HasValue && st == ShotType.Normal))
                    {
                        // Initialize a new instance of the SpeechSynthesizer.  
                        SpeechSynthesizer synth = new SpeechSynthesizer();
                        // Set a value for the speaking rate.  
                        synth.Rate = 1;
                        synth.Volume = 100;
                        // Configure the audio output.   
                        synth.SetOutputToDefaultAudioDevice();

                        if (st.HasValue && st == ShotType.Normal)
                        {
                            // Speak a text string synchronously.  
                            synth.Speak("Misread occurred. Please try again.");
                        }                        
                    }
                    if (playSoundOnPracticeSwing && (st.HasValue && st == ShotType.Practice))
                    {
                        // Initialize a new instance of the SpeechSynthesizer.  
                        using (SpeechSynthesizer synth = new SpeechSynthesizer())
                        {
                            // Set a value for the speaking rate.  
                            synth.Rate = 1;
                            synth.Volume = 100;
                            // Configure the audio output.   
                            synth.SetOutputToDefaultAudioDevice();                                                       
                            // Speak a text string synchronously.  
                            synth.Speak(clubData?.Speed.ToString() +  " miles per hour.");                            
                        }
                            
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