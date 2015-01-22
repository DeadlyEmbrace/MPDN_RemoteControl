using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Timers;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;

namespace MPDN_RemoteControl
{
    /// <summary>
    /// Interaction logic for RemoteControl.xaml
    /// </summary>
    public partial class RemoteControl : Window
    {
        #region Variables
        private Socket _server;
        private StreamWriter _writer;
        private Guid _myGuid;
        private Guid _clientAuthGuid;
        private string _playState = "None";
        private TimeSpan _duration;
        private bool _movingSlider = false;
        private string _currentFile;
        private bool _isFullscreen = false;
        private bool _muted = false;
        private readonly ClientGuid _guidManager = new ClientGuid();
        #endregion

        #region Constuctor
        public RemoteControl()
        {
            InitializeComponent();
            _clientAuthGuid = _guidManager.GetGuid;
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
                    BtnConnect.IsEnabled = isEnabled;
                    BtnDisconnect.IsEnabled = !isEnabled;
                    TxbIp.IsEnabled = isEnabled;
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
                    SldrSpan.IsEnabled = isEnabled;
                    BtnBrowse.IsEnabled = isEnabled;
                    BtnPlayPause.IsEnabled = isEnabled;
                    BtnStop.IsEnabled = isEnabled;
                    BtnFullscreen.IsEnabled = isEnabled;
                    BtnMute.IsEnabled = isEnabled;
                });
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            SetConnectButtonState(false);
            string ip = TxbIp.Text;
            if (!String.IsNullOrEmpty(ip))
            {
                var addr = ip.Split(':');
                if (addr.Count() == 2)
                {
                    IPAddress ipAddr;
                    IPAddress.TryParse(addr[0], out ipAddr);
                    int port = 0;
                    int.TryParse(addr[1], out port);
                    if (port != 0 && ipAddr != null)
                    {
                        Task.Run(() => ClientConnect(ipAddr, port));
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
                _server = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                _server.Connect(serverEndpoint);
                NetworkStream nStream = new NetworkStream(_server);
                StreamReader reader = new StreamReader(nStream);
                _writer = new StreamWriter(nStream);

                Dispatcher.Invoke(() => LblState.Content = "Auth Code:" + _clientAuthGuid.ToString());
                PassCommandToServer(_clientAuthGuid.ToString());


                Dispatcher.Invoke(() =>
                {
                    BtnBrowse.IsEnabled = true;
                    BtnDisconnect.IsEnabled = true;
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
                    case "Exit":
                        ForcedDisconnect();
                        break;
                    case "ClientGUID":
                        if (Guid.TryParse(cmd[1], out _myGuid))
                        {
                            Dispatcher.Invoke(() => LblStatus.Content = "Status: Connected");
                            PassCommandToServer("GetCurrentState|" + _myGuid.ToString());
                        }
                        break;
                    case "Playing":
                        Dispatcher.Invoke(() =>
                        {
                            _currentFile = cmd[1];
                            LblFile.Content = cmd[1];
                            LblState.Content = "Playing";
                            _playState = "Playing";
                            BtnPlayPause.Content = "Pause";
                            BtnPlayPause.IsEnabled = true;
                            SldrSpan.IsEnabled = true;
                            BtnStop.IsEnabled = true;
                            BtnFullscreen.IsEnabled = true;
                            BtnMute.IsEnabled = true;
                        });
                        PassCommandToServer("GetDuration|" + _myGuid.ToString());
                        break;
                    case "Paused":
                        Dispatcher.Invoke(() =>
                        {
                            LblFile.Content = cmd[1];
                            LblState.Content = "Paused";
                            _playState = "Paused";
                            BtnPlayPause.Content = "Play";
                        });
                        break;
                    case "Stopped":
                        Dispatcher.Invoke(() =>
                        {
                            LblFile.Content = cmd[1];
                            LblState.Content = "Stopped";
                            _playState = "Stopped";
                            BtnPlayPause.Content = "Play";
                            BtnStop.IsEnabled = false;
                        });
                        break;
                    case "Closed":
                        Dispatcher.Invoke(() =>
                            {
                                LblFile.Content = "None";
                                LblState.Content = "Disconnected";
                                _playState = "Disconnected";
                                BtnPlayPause.Content = "Play";
                                _currentFile = String.Empty;
                            });
                        break;
                    case "Closing":
                        Dispatcher.Invoke(() =>
                            {
                                _currentFile = String.Empty;
                                LblFile.Content = "None";
                                LblState.Content = "Disconnected";
                                _playState = "Disconnected";
                                BtnPlayPause.Content = "Play";
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
                                        string strDuration = span.Hours.ToString("00") + ":" + span.Minutes.ToString("00") + ":" + span.Seconds.ToString("00") + "\\" + _duration;
                                        SldrSpan.Value = span.TotalSeconds;
                                        LblPosition.Content = strDuration;

                                });
                        }
                        catch (Exception)
                        {

                        }
                        break;
                    case "FullLength":
                        long fullDur = 0;
                        long.TryParse(cmd[1], out fullDur);
                        _duration = TimeSpan.FromMilliseconds(fullDur / 1000);
                        Dispatcher.Invoke(() => SldrSpan.Maximum = _duration.TotalSeconds);
                        break;
                    case "Fullscreen":
                        Dispatcher.Invoke(() =>
                            {
                                bool fs = false;
                                Boolean.TryParse(cmd[1].ToString(), out fs);
                                _isFullscreen = fs;
                                if(_isFullscreen)
                                {
                                    BtnFullscreen.Content = "Exit Fullscreen";
                                }
                                else
                                {
                                    BtnFullscreen.Content = "Go Fullscreen";
                                }
                            });
                        break;
                    case "Mute":
                        Dispatcher.Invoke(() =>
                        {
                            bool muted = false;
                            Boolean.TryParse(cmd[1], out muted);
                            _muted = muted;
                            if (muted)
                            {
                                BtnMute.Content = "Unmute";
                            }
                            else
                            {
                                BtnMute.Content = "Mute";
                            }
                        });
                        break;
                    case "Volume":
                        Dispatcher.Invoke(() =>
                        {
                            int vol = -1;
                            int.TryParse(cmd[1], out vol);
                            if (vol >= 0)
                            {
                                SldrVolume.ValueChanged -= sldrVolume_ValueChanged;
                                SldrVolume.Value = vol;
                                LblLevel.Content = vol;
                                SldrVolume.ValueChanged += sldrVolume_ValueChanged;
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

        private void sldrVolume_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            LblLevel.Content = (int)SldrVolume.Value;
            PassCommandToServer("Volume|" + (int)SldrVolume.Value);
        }

        /// <summary>
        /// Should be called when the user or the server closes the connection
        /// </summary>
        private void CloseConnection()
        {
            _writer.Close();
            _server.Close();
            _myGuid = Guid.Empty;
            _writer = null;
            _server = null;
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
                BtnPlayPause.IsEnabled = true;
            }
        }

        /// <summary>
        /// Send a command to the attached server
        /// </summary>
        /// <param name="cmd"></param>
        private void PassCommandToServer(string cmd)
        {
            try
            {
                if (_writer != null)
                {
                    _writer.WriteLine(cmd);
                    _writer.Flush();
                }
            }
            catch (Exception)
            {
            }
        }

        private void ForcedDisconnect()
        {
            SetConnectButtonState(true);
            SetPlaybackButtonState(false);
            Dispatcher.Invoke(() => LblStatus.Content = "Status: Not Connected - Not Authorized");
            _writer.Close();
            _writer = null;
            _server.Close();
            _server = null;
        }

        private void btnPlayPause_Click(object sender, RoutedEventArgs e)
        {
            if(_playState == "Stopped" || _playState == "Paused")
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
            if (_movingSlider)
            {
                PassCommandToServer("Seek|" + SldrSpan.Value * 1000000);
            }
        }

        private void sldrSpan_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _movingSlider = true;
            PassCommandToServer("Pause|False");
        }

        private void sldrSpan_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _movingSlider = false;
            PassCommandToServer("Play|False");
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            PassCommandToServer("Exit|" + _myGuid);
        }

        private void btnDisconnect_Click(object sender, RoutedEventArgs e)
        {
            PassCommandToServer("Exit|" + _myGuid);
            SetConnectButtonState(true);
            SetPlaybackButtonState(false);
            LblStatus.Content = "Status: Not Connected";
        }

        private void btnFullscreen_Click(object sender, RoutedEventArgs e)
        {
            if(!_isFullscreen)
                PassCommandToServer("FullScreen|True");
            else
                PassCommandToServer("FullScreen|False");
        }

        private void BtnMute_Click(object sender, RoutedEventArgs e)
        {
            if(!_muted)
                PassCommandToServer("Mute|True");
            else
                PassCommandToServer("Mute|False");
        }
    }
}
