using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
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
        private long _currenLocation;
        private bool _movingSlider = false;
        private string _currentFile;
        private bool _isFullscreen = false;
        private bool _muted = false;
        private readonly ClientGuid _guidManager = new ClientGuid();
        private ObservableCollection<Chapter> _showChapters = new ObservableCollection<Chapter>();
        private ObservableCollection<Subtitles> _showSubtitles = new ObservableCollection<Subtitles>();
        private ObservableCollection<Audio> _audioTracks = new ObservableCollection<Audio>(); 
        #endregion

        #region Constuctor
        public RemoteControl()
        {
            InitializeComponent();
            this.DataContext = this;
            _clientAuthGuid = _guidManager.GetGuid;
            LoadVersionNumber();
        }
        #endregion

        #region Properties
        public ObservableCollection<Chapter> ShowChapters
        {
            get { return _showChapters;}
            set { _showChapters = value; }
        }

        public ObservableCollection<Subtitles> ShowSubtitles
        {
            get { return _showSubtitles; }
            set { _showSubtitles = value; }
        }

        public ObservableCollection<Audio> ShowAudioTracks
        {
            get { return _audioTracks; }
            set { _audioTracks = value; }
        }

        #endregion

        private void LoadVersionNumber()
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            FileVersionInfo fvi = FileVersionInfo.GetVersionInfo(assembly.Location);
            string version = "v" + fvi.FileMajorPart + "." + fvi.FileMinorPart + "." + fvi.FileBuildPart;
            LblVersion.Content = version;
        }

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
                    if (BtnPlayPause.IsEnabled != isEnabled)
                    {
                        SldrSpan.IsEnabled = isEnabled;
                        BtnBrowse.IsEnabled = isEnabled;
                        BtnAddToPlaylist.IsEnabled = isEnabled;
                        BtnPlayPause.IsEnabled = isEnabled;
                        BtnStop.IsEnabled = isEnabled;
                        BtnFullscreen.IsEnabled = isEnabled;
                        BtnMute.IsEnabled = isEnabled;
                        SldrVolume.IsEnabled = isEnabled;
                    }
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

                //Dispatcher.Invoke(() => LblState.Content = "Auth Code:" + _clientAuthGuid.ToString());
                PassCommandToServer(_clientAuthGuid.ToString());


                Dispatcher.Invoke(() =>
                {
                    BtnBrowse.IsEnabled = true;
                    BtnAddToPlaylist.IsEnabled = true;
                    BtnDisconnect.IsEnabled = true;
                    SldrVolume.IsEnabled = true;
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
                            Dispatcher.Invoke(() =>
                            {
                                LblStatus.Content = "Status: Connected";
                                LblState.Content = "Connected";
                            });
                            PassCommandToServer("GetCurrentState|" + _myGuid.ToString());
                        }
                        break;
                    case "AuthCode":
                        Guid.TryParse(cmd[1], out _myGuid);
                        break;
                    case "Connected":
                        Dispatcher.Invoke(() =>
                        {
                            LblStatus.Content = "Status: Connected";
                            LblState.Content = "Connected";
                        });
                        PassCommandToServer("GetCurrentState|" + _myGuid);
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
                            SetPlaybackButtonState(true);
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
                                //LblState.Content = "Disconnected";
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
                                //LblState.Content = "Disconnected";
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

                                    _currenLocation = 0;
                                    long.TryParse(cmd[1], out _currenLocation);
                                    var span = TimeSpan.FromTicks(_currenLocation * 10);
                                    var currChapter = _showChapters.FirstOrDefault(t => t.ChapterLocation >= _currenLocation);
                                    if (currChapter != null && cbChapters.SelectedIndex != currChapter.ChapterIndex)
                                    {
                                        cbChapters.SelectionChanged -= cbChapters_SelectionChanged;
                                        cbChapters.SelectedIndex = (currChapter.ChapterIndex - 2);
                                        cbChapters.SelectionChanged += cbChapters_SelectionChanged;
                                    }

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
                    case "Chapters":
                        DisplayChapters(cmd[1]);
                        break;
                    case "Subtitles":
                        DisplaySubtitles(cmd[1]);
                        break;
                    case "SubChanged":
                        ChangeActiveSubtitles(cmd[1]);
                        break;
                    case "AudioTracks":
                        DisplayAudioTracks(cmd[1]);
                        break;
                    case "AudioChanged":
                        break;
                }
            }
            else
            {
                //Invalid command
            }
        }

        private void DisplayAudioTracks(string audioTracks)
        {
            try
            {
                Dispatcher.Invoke(() => _audioTracks.Clear());
                var audioSubstrings = Regex.Split(audioTracks, "]]");
                foreach (var track in audioSubstrings)
                {
                    var splitData = Regex.Split(track, ">>");

                    int trNumber = -1;
                    int.TryParse(splitData[0], out trNumber);
                    bool isActive = false;
                    Boolean.TryParse(splitData[3], out isActive);
                    if (trNumber > 0)
                    {
                        Audio tmpAudio = new Audio()
                        {
                            Description = splitData[1],
                            Type = splitData[2],
                            Active = isActive
                        };
                        Dispatcher.Invoke(() => _audioTracks.Add(tmpAudio));
                    }
                }
                UpdateAudioControl();
            }
            catch (Exception)
            {
                throw;
            }
        }

        private void ChangeActiveAudioTrack(string track)
        {
            var currentTrack = _audioTracks.FirstOrDefault(t => t.Active);
            if (currentTrack != null && currentTrack.Description != track)
            {
                currentTrack.Active = false;
                var newTrack = _audioTracks.FirstOrDefault(t => t.Description == track);
                if (newTrack != null)
                    newTrack.Active = true;
                UpdateAudioControl();
            }
        }

        private void UpdateAudioControl()
        {
            Dispatcher.Invoke(() =>
            {
                if (_audioTracks.Count > 0)
                {
                    cbAudio.IsEnabled = true;
                    cbAudio.SelectionChanged -= cbAudio_SelectionChanged;
                    cbAudio.SelectedItem = _audioTracks.FirstOrDefault(t => t.Active);
                    cbAudio.SelectionChanged += cbAudio_SelectionChanged;
                }
                else
                    cbAudio.IsEnabled = false;

            });
        }


        private void ChangeActiveSubtitles(string subDesc)
        {
            var currentSub = _showSubtitles.FirstOrDefault(t => t.ActiveSub);
            if(currentSub != null && currentSub.SubtitleDesc != subDesc)
            {
                currentSub.ActiveSub = false;
                var newSubs = _showSubtitles.FirstOrDefault(t => t.SubtitleDesc == subDesc);
                if(newSubs != null)
                    newSubs.ActiveSub = true;
                UpdateSubControl();
            }

        }

        private void UpdateSubControl()
        {
            Dispatcher.Invoke(() =>
            {
                if (_showSubtitles.Count > 0)
                {
                    cbSubtitles.IsEnabled = true;
                    cbSubtitles.SelectionChanged -= cbSubtitles_SelectionChanged;
                    cbSubtitles.SelectedItem = _showSubtitles.FirstOrDefault(t => t.ActiveSub);
                    cbSubtitles.SelectionChanged += cbSubtitles_SelectionChanged;
                }
                else
                    cbSubtitles.IsEnabled = false;
            });
        }

        private void DisplaySubtitles(string subs)
        {
            try
            {
                Dispatcher.Invoke(() => _showSubtitles.Clear());
                var subStrings = Regex.Split(subs, "]]");
                foreach(var sub in subStrings)
                {
                    var splitData = Regex.Split(sub, ">>");

                    int subNumber = -1;
                    int.TryParse(splitData[0], out subNumber);
                    bool isActive = false;
                    Boolean.TryParse(splitData[3], out isActive);
                    if(subNumber > 0)
                    {
                        Subtitles tmpSub = new Subtitles() { SubtitleDesc = splitData[1], SubtitleType = splitData[2], ActiveSub = isActive };
                        Dispatcher.Invoke(() => _showSubtitles.Add(tmpSub));
                    }
                }
                UpdateSubControl();
            }
            catch(Exception)
            {
                throw;
            }
        }

        private void DisplayChapters(string chapters)
        {
            try
            {
                Dispatcher.Invoke(() => _showChapters.Clear());
                var chapterStrings = Regex.Split(chapters, "]]");
                foreach (var singleChapter in chapterStrings)
                {
                    var splitData = Regex.Split(singleChapter, ">>");
                    int chapterNumber = -1;
                    int.TryParse(splitData[0], out chapterNumber);
                    if (chapterNumber > 0)
                    {
                        long loc = -1;
                        long.TryParse(splitData[2], out loc);
                        Chapter tmpChapter = new Chapter() {ChapterIndex = chapterNumber, ChapterName = splitData[1], ChapterLocation = loc};
                        Dispatcher.Invoke(() => _showChapters.Add(tmpChapter));
                    }
                }
                Dispatcher.Invoke(() =>
                {
                    if (_showChapters.Count > 0)
                    {
                        var currChapter = _showChapters.FirstOrDefault(t => t.ChapterLocation >= _currenLocation);
                        if (currChapter != null && cbChapters.SelectedIndex != currChapter.ChapterIndex)
                        {
                            cbChapters.SelectionChanged -= cbChapters_SelectionChanged;
                            cbChapters.SelectedIndex = (currChapter.ChapterIndex - 2);
                            cbChapters.SelectionChanged += cbChapters_SelectionChanged;
                        }
                        cbChapters.IsEnabled = true;
                    }
                    else
                        cbChapters.IsEnabled = false;
                });

            }
            catch (Exception ex)
            {
                
                throw;
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
            cbChapters.SelectionChanged -= cbChapters_SelectionChanged;
            _showChapters.Clear();
            cbChapters.SelectionChanged += cbChapters_SelectionChanged;
            cbSubtitles.SelectionChanged -= cbSubtitles_SelectionChanged;
            _showSubtitles.Clear();
            cbSubtitles.SelectionChanged -= cbSubtitles_SelectionChanged;
            cbAudio.SelectionChanged -= cbAudio_SelectionChanged;
            _audioTracks.Clear();
            cbAudio.SelectionChanged += cbAudio_SelectionChanged;
            cbChapters.IsEnabled = false;
            cbSubtitles.IsEnabled = false;
            SldrVolume.IsEnabled = false;
            cbAudio.IsEnabled = false;
            _duration = new TimeSpan(0,0,0,0);
            _currentFile = String.Empty;
            LblPosition.Content = "00:00:00";
            LblFile.Content = "None";
            LblState.Content = "Not Connected";
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

        private void cbChapters_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var item = cbChapters.SelectedItem as Chapter;
            if(item != null)
                PassCommandToServer("Seek|" + item.ChapterLocation);
        }

        private void cbSubtitles_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var sub = cbSubtitles.SelectedItem as Subtitles;
            if(sub != null)
                PassCommandToServer("ActiveSubTrack|" + sub.SubtitleDesc);
        }

        private void cbAudio_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {

        }

        private void BtnAddToPlaylist_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFile = new OpenFileDialog();
            openFile.Multiselect = true;
            openFile.Title = "Select File(s) to Play";
            if ((bool)openFile.ShowDialog())
            {
                var files = openFile.FileNames;
                StringBuilder sb = new StringBuilder();
                int counter = 1;
                foreach (var file in files)
                {
                    if (counter > 1)
                        sb.Append(">>");
                    sb.Append(file);
                    counter++;
                }
                PassCommandToServer("AddFilesToPlaylist|" + sb);
            }
        }

        //private void BtnSeek_Click(object sender, RoutedEventArgs e)
        //{
        //    var seekText = TxbSeekTime.Text;
        //    if (!String.IsNullOrEmpty(seekText))
        //    {
        //        double seekDur = -1;
        //        Double.TryParse(seekText, out seekDur);
        //        if (seekDur >= 0)
        //        {
        //            PassCommandToServer("Seek|" + seekDur);
        //        }
        //    }
        //}
    }
}
