using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Timers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace MPDN_RemoteControl
{
    /// <summary>
    /// Interaction logic for RemoteControl.xaml
    /// </summary>
    public partial class RemoteControl : Window
    {
        #region Variables
        private Socket server;
        private StreamWriter writer;
        private Guid myGuid;
        private string playState = "None";
        private Timer DurationTimer;
        private TimeSpan Duration;
        private bool movingSlider = false;
        private string currentFile;
        private bool isFullscreen = false;
        #endregion

        #region Constuctor
        public RemoteControl()
        {
            InitializeComponent();
            DurationTimer = new Timer(100);
            DurationTimer.Elapsed += DurationTimer_Elapsed;
            DurationTimer.Start();
        }
        #endregion

        /// <summary>
        /// Set the state of the Connect Button
        /// </summary>
        /// <param name="isEnabled">Is button active</param>
        private void SetConnectButtonState(bool isEnabled)
        {
            Dispatcher.Invoke(() =>
                {
                    btnConnect.IsEnabled = isEnabled;
                    btnDisconnect.IsEnabled = !isEnabled;
                    txbIP.IsEnabled = isEnabled;
                });
        }

       /// <summary>
       /// Set the playback control's state
       /// </summary>
       /// <param name="isEnabled">Are controls active</param>
        private void SetPlaybackButtonState(bool isEnabled)
        {
            Dispatcher.Invoke(() =>
                {
                    sldrSpan.IsEnabled = isEnabled;
                    btnBrowse.IsEnabled = isEnabled;
                    btnPlayPause.IsEnabled = isEnabled;
                    btnStop.IsEnabled = isEnabled;
                    btnFullscreen.IsEnabled = isEnabled;
                });
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            SetConnectButtonState(false);
            string IP = txbIP.Text;
            if (!String.IsNullOrEmpty(IP))
            {
                var addr = IP.Split(':');
                if (addr.Count() == 2)
                {
                    IPAddress IPAddr;
                    IPAddress.TryParse(addr[0], out IPAddr);
                    int port = 0;
                    int.TryParse(addr[1], out port);
                    if (port != 0 && IPAddr != null)
                    {
                        Task.Run(() => ClientConnect(IPAddr, port));
                    }
                    else
                    {
                        MessageBox.Show("Please enter an IP address and port", "Invalid IP Address", MessageBoxButton.OK, MessageBoxImage.Error);
                        SetConnectButtonState(true);
                    }
                }
                else
                {
                    MessageBox.Show("Please enter an IP address", "Invalid IP Address", MessageBoxButton.OK, MessageBoxImage.Error);
                    SetConnectButtonState(true);
                }
            }
            else
            {
                MessageBox.Show("Please enter an IP address", "No IP Address", MessageBoxButton.OK, MessageBoxImage.Error);
                SetConnectButtonState(true);
            }
        }

        private void ClientConnect(IPAddress ip, int port)
        {
            try
            {
                IPEndPoint serverEndpoint = new IPEndPoint(ip, port);
                server = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                server.Connect(serverEndpoint);
                NetworkStream nStream = new NetworkStream(server);
                StreamReader reader = new StreamReader(nStream);
                writer = new StreamWriter(nStream);
                Dispatcher.Invoke(() =>
                {
                    btnBrowse.IsEnabled = true;
                    btnDisconnect.IsEnabled = true;
                });
                while (true)
                {
                    try
                    {
                        var data = reader.ReadLine();
                        if (!String.IsNullOrEmpty(data))
                        {
                            Task.Run(() => HandleServerComms(data));
                        }
                    }
                    catch (Exception)
                    {
                        break;
                    }
                }
            }
            catch(SocketException)
            {
                MessageBox.Show("No connection could be made to the server.\r\nPlease ensure IP is correct and that the server is running then try again", "Connection Error", MessageBoxButton.OK, MessageBoxImage.Error);
                SetConnectButtonState(true);
            }
            
        }

        /// <summary>
        /// Handle a command that has been retrieved from the server
        /// </summary>
        /// <param name="command"></param>
        private void HandleServerComms(string command)
        {
            var cmd = command.Split('|');
            if (cmd.Count() == 2)
            {
                switch (cmd[0])
                {
                    case "ClientGUID":
                        if (Guid.TryParse(cmd[1], out myGuid))
                        {
                            Dispatcher.Invoke(() => lblStatus.Content = "Status: Connected");
                            PassCommandToServer("GetCurrentState|" + myGuid.ToString());
                        }
                        break;
                    case "Playing":
                        Dispatcher.Invoke(() =>
                        {
                            currentFile = cmd[1];
                            lblFile.Content = cmd[1];
                            lblState.Content = "Playing";
                            playState = "Playing";
                            btnPlayPause.Content = "Pause";
                            btnPlayPause.IsEnabled = true;
                            sldrSpan.IsEnabled = true;
                            btnStop.IsEnabled = true;
                            btnFullscreen.IsEnabled = true;
                        });
                        PassCommandToServer("GetDuration|" + myGuid.ToString());
                        break;
                    case "Paused":
                        Dispatcher.Invoke(() =>
                        {
                            lblFile.Content = cmd[1];
                            lblState.Content = "Paused";
                            playState = "Paused";
                            btnPlayPause.Content = "Play";
                        });
                        break;
                    case "Stopped":
                        Dispatcher.Invoke(() =>
                        {
                            lblFile.Content = cmd[1];
                            lblState.Content = "Stopped";
                            playState = "Stopped";
                            btnPlayPause.Content = "Play";
                            btnStop.IsEnabled = false;
                        });
                        break;
                    case "Closed":
                        Dispatcher.Invoke(() =>
                            {
                                lblFile.Content = "None";
                                lblState.Content = "Disconnected";
                                playState = "Disconnected";
                                btnPlayPause.Content = "Play";
                                currentFile = String.Empty;
                            });
                        break;
                    case "Closing":
                        Dispatcher.Invoke(() =>
                            {
                                currentFile = String.Empty;
                                lblFile.Content = "None";
                                lblState.Content = "Disconnected";
                                playState = "Disconnected";
                                btnPlayPause.Content = "Play";
                                CloseConnection();
                            });
                        break;
                    case "Postion":
                        try
                        {
                            Dispatcher.Invoke(() =>
                                {

                                        long dur = 0;
                                        long.TryParse(cmd[1], out dur);
                                        var span = TimeSpan.FromTicks(dur * 10);
                                        string strDuration = span.Hours.ToString("00") + ":" + span.Minutes.ToString("00") + ":" + span.Seconds.ToString("00") + "\\" + Duration;
                                        sldrSpan.Value = span.TotalSeconds;
                                        lblPosition.Content = strDuration;

                                });
                        }
                        catch (Exception)
                        {

                        }
                        break;
                    case "FullLength":
                        long fullDur = 0;
                        long.TryParse(cmd[1], out fullDur);
                        Duration = TimeSpan.FromMilliseconds(fullDur / 1000);
                        Dispatcher.Invoke(() => sldrSpan.Maximum = Duration.TotalSeconds);
                        break;
                    case "Fullscreen":
                        Dispatcher.Invoke(() =>
                            {
                                bool fs = false;
                                Boolean.TryParse(command[1].ToString(), out fs);
                                isFullscreen = fs;
                                if(isFullscreen)
                                {
                                    btnFullscreen.Content = "Exit Fullscreen";
                                }
                                else
                                {
                                    btnFullscreen.Content = "Go Fullscreen";
                                }
                            });
                        break;
                }
            }
            else
            {
                //Invalid command
            }
        }

        /// <summary>
        /// DurationTimer elapsed
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        void DurationTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            if (playState == "Playing")
            {
                GetLocation();
            }
        }

        /// <summary>
        /// Request the current playback location from MPDN
        /// </summary>
        private void GetLocation()
        {
            writer.WriteLine("GetLocation|" + myGuid.ToString());
            writer.Flush();
        }

        /// <summary>
        /// Should be called when the user or the server closes the connection
        /// </summary>
        private void CloseConnection()
        {
            writer.Close();
            server.Close();
            myGuid = Guid.Empty;
            writer = null;
            server = null;
            SetConnectButtonState(true);
            SetPlaybackButtonState(false);
        }

        private void btnBrowse_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFile = new OpenFileDialog();
            openFile.Title = "Select File to Play";
            if((bool)openFile.ShowDialog())
            {
                var file = openFile.FileName;
                PassCommandToServer("Open|" + openFile.FileName);
                btnPlayPause.IsEnabled = true;
            }
        }

        /// <summary>
        /// Send a command to the attached server
        /// </summary>
        /// <param name="cmd"></param>
        private void PassCommandToServer(string cmd)
        {
            if (writer != null)
            {
                writer.WriteLine(cmd);
                writer.Flush();
            }
        }

        private void btnPlayPause_Click(object sender, RoutedEventArgs e)
        {
            if(playState == "Stopped" || playState == "Paused")
            {
                PassCommandToServer("Play|False");
            }
            else
            {
                PassCommandToServer("Pause|False");
            }
        }

        private void btnStop_Click(object sender, RoutedEventArgs e)
        {
            /*
             * This currently causes a weird issue when you try to play again
             */
            PassCommandToServer("Stop|False");
        }


        private void sldrSpan_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (movingSlider)
            {
                PassCommandToServer("Seek|" + sldrSpan.Value * 1000000);
            }
        }

        private void sldrSpan_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            movingSlider = true;
            PassCommandToServer("Pause|False");
        }

        private void sldrSpan_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            movingSlider = false;
            PassCommandToServer("Play|False");
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            PassCommandToServer("Exit|" + myGuid);
        }

        private void btnDisconnect_Click(object sender, RoutedEventArgs e)
        {
            PassCommandToServer("Exit|" + myGuid);
            SetConnectButtonState(true);
            SetPlaybackButtonState(false);
            lblStatus.Content = "Status: Not Connected";
        }

        private void btnFullscreen_Click(object sender, RoutedEventArgs e)
        {
            if(!isFullscreen)
                PassCommandToServer("FullScreen|True");
            else
                PassCommandToServer("FullScreen|False");
        }
    }
}
