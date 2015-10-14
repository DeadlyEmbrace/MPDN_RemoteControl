using System;
using System.Collections.Generic;
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
using System.Windows.Media.Imaging;
using GongSolutions.Wpf.DragDrop;
using Microsoft.Win32;
using MPDN_RemoteControl.Controls;
using MPDN_RemoteControl.Objects;

namespace MPDN_RemoteControl
{
    /// <summary>
    /// Interaction logic for RemoteControl.xaml
    /// </summary>
    public partial class RemoteControl : IDropTarget
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
        readonly ObservableCollection<KeyValuePair<string, bool>> _playlistContent = new ObservableCollection<KeyValuePair<string, bool>>();
        private readonly object _videoLock = new object();

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
        public ObservableCollection<Chapter> ShowChapters { get; set; } = new ObservableCollection<Chapter>();
        public ObservableCollection<Subtitles> ShowSubtitles { get; set; } = new ObservableCollection<Subtitles>();
        public ObservableCollection<Audio> ShowAudioTracks { get; set; } = new ObservableCollection<Audio>();
        public ObservableCollection<Video> ShowVideoTracks { get; set; } = new ObservableCollection<Video>();

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
                    BtnPlaylistShow.IsEnabled = true;
                    BtnUrl.IsEnabled = true;
                    BtnClear.IsEnabled = true;
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
                        HandleClientGuid(cmd[1]);
                        break;
                    case "AuthCode":
                        Guid.TryParse(cmd[1], out _myGuid);
                        break;
                    case "Connected":
                        HandleConnected(cmd[1]);
                        break;
                    case "Playing":
                        HandlePlay(cmd[1]);
                        break;
                    case "Paused":
                        HandlePaused(cmd[1]);
                        break;
                    case "Stopped":
                        HandleStopped(cmd[1]);
                        break;
                    case "Closed":
                        HandleClosed(cmd[1]);
                        break;
                    case "Closing":
                        HandleClosing(cmd[1]);
                        break;
                    case "Postion":
                        HandlePosition(cmd[1]);
                        break;
                    case "FullLength":
                        HandleLength(cmd[1]);
                        break;
                    case "Fullscreen":
                        HandleFullscreen(cmd[1]);
                        break;
                    case "Mute":
                        HandleMute(cmd[1]);
                        break;
                    case "Volume":
                        HandleVolume(cmd[1]);
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
                        ChangeActiveAudioTrack(cmd[1]);
                        break;
                    case "PlaylistShow":
                        PlaylistStateChanged(cmd[1]);
                        break;
                    case "PlaylistContent":
                        ShowPlaylistContent(cmd[1]);
                        break;
                    case "VideoTracks":
                        DisplayVideoTracks(cmd[1]);
                        break;
                    case "VideoChanged":
                        ChangeActiveVideoTrack(cmd[1]);
                        break;
                }
            }
            else
            {
                //Invalid command
            }
        }

        private void ShowPlaylistContent(string cmd)
        {
            var items = Regex.Split(cmd, ">>");
            Dispatcher.Invoke(() => _playlistContent.Clear());
            foreach (var item in items)
            {
                var finalSplit = Regex.Split(item, "]]");
                if (finalSplit.Count() == 2)
                {
                    KeyValuePair<string, bool> tmpItem = new KeyValuePair<string, bool>(finalSplit[0],
                    bool.Parse(finalSplit[1]));
                    Dispatcher.Invoke(() => _playlistContent.Add(tmpItem));
                }
            }

            Dispatcher.Invoke(() =>
            {
                if (_playlistContent.Count > 1)
                {
                    BtnPrevious.IsEnabled = true;
                    BtnNext.IsEnabled = true;
                }
                else
                {
                    BtnPrevious.IsEnabled = false;
                    BtnNext.IsEnabled = false;
                }

                DataGridPlaylist.ItemsSource = _playlistContent;
            });
        }

        private void PlaylistStateChanged(string cmd)
        {
            Dispatcher.Invoke(() =>
            {
                if (cmd == "True")
                {
                    Dispatcher.Invoke(() =>
                    {
                        BtnPlaylistShow.ToolTip = "Hide";
                        ImgShow.Source = new BitmapImage(new Uri("pack://application:,,,/MPDN_RemoteControl;component/Icons/Hide.png"));
                    });
                }
                else
                {
                    BtnPlaylistShow.ToolTip = "Show";
                    ImgShow.Source = new BitmapImage(new Uri("pack://application:,,,/MPDN_RemoteControl;component/Icons/Show.png"));
                }
            });
        }

        private void HandleClientGuid(string command)
        {
            if (Guid.TryParse(command, out _myGuid))
            {
                Dispatcher.Invoke(() =>
                {
                    LblStatus.Content = "Status: Connected";
                    LblState.Content = "Connected";
                });
                PassCommandToServer("GetCurrentState|" + _myGuid);
            }
        }

        private void HandleConnected(string command)
        {
            Dispatcher.Invoke(() =>
            {
                LblStatus.Content = "Status: Connected";
                LblState.Content = "Connected";
            });
            PassCommandToServer("GetCurrentState|" + _myGuid);
        }

        private void HandlePlay(string command)
        {
            Dispatcher.Invoke(() =>
            {
                _currentFile = command;
                LblFile.Content = command;
                LblState.Content = "Playing";
                _playState = "Playing";
                BtnPlayPause.ToolTip = "Pause";
                ImgPlayPause.Source = new BitmapImage(new Uri("pack://application:,,,/MPDN_RemoteControl;component/Icons/Pause.png"));
                BtnPlayPause.IsEnabled = true;
                SldrSpan.IsEnabled = true;
                BtnStop.IsEnabled = true;
                BtnFullscreen.IsEnabled = true;
                BtnMute.IsEnabled = true;
            });
            PassCommandToServer("GetDuration|" + _myGuid);
        }

        private void HandlePaused(string command)
        {
            Dispatcher.Invoke(() =>
            {
                LblFile.Content = command;
                LblState.Content = "Paused";
                _playState = "Paused";
                BtnPlayPause.ToolTip = "Play";
                ImgPlayPause.Source = new BitmapImage(new Uri("pack://application:,,,/MPDN_RemoteControl;component/Icons/Play.png"));
                SetPlaybackButtonState(true);
            });
        }

        private void HandleStopped(string command)
        {
            Dispatcher.Invoke(() =>
            {
                LblFile.Content = command;
                LblState.Content = "Stopped";
                _playState = "Stopped";
                BtnPlayPause.ToolTip = "Play";
                ImgPlayPause.Source = new BitmapImage(new Uri("pack://application:,,,/MPDN_RemoteControl;component/Icons/Play.png"));
                BtnStop.IsEnabled = false;
            });
        }

        private void HandleClosed(string command)
        {
            Dispatcher.Invoke(() =>
            {
                LblFile.Content = "None";
                //LblState.Content = "Disconnected";
                _playState = "Disconnected";
                BtnPlayPause.ToolTip = "Play";
                ImgPlayPause.Source = new BitmapImage(new Uri("pack://application:,,,/MPDN_RemoteControl;component/Icons/Play.png"));
                _currentFile = string.Empty;
            });
        }

        private void HandleClosing(string command)
        {
            Dispatcher.Invoke(() =>
            {
                _currentFile = String.Empty;
                LblFile.Content = "None";
                //LblState.Content = "Disconnected";
                _playState = "Disconnected";
                BtnPlayPause.ToolTip = "Play";
                ImgPlayPause.Source = new BitmapImage(new Uri("pack://application:,,,/MPDN_RemoteControl;component/Icons/Play.png"));
                CloseConnection();
            });
        }

        private void HandlePosition(string command)
        {
            try
            {
                Dispatcher.Invoke(() =>
                {

                    _currenLocation = 0;
                    long.TryParse(command, out _currenLocation);
                    var span = TimeSpan.FromTicks(_currenLocation * 10);
                    var currChapter = ShowChapters.FirstOrDefault(t => t.ChapterLocation >= _currenLocation);
                    if (currChapter != null && CbChapters.SelectedIndex != currChapter.ChapterIndex)
                    {
                        CbChapters.SelectionChanged -= cbChapters_SelectionChanged;
                        CbChapters.SelectedIndex = (currChapter.ChapterIndex - 2);
                        CbChapters.SelectionChanged += cbChapters_SelectionChanged;
                    }

                    string strDuration = span.Hours.ToString("00") + ":" + span.Minutes.ToString("00") + ":" + span.Seconds.ToString("00") + "\\" + _duration;
                    SldrSpan.Value = span.TotalSeconds;
                    LblPosition.Content = strDuration;

                });
            }
            catch (Exception)
            {

            }
        }

        private void HandleLength(string command)
        {
            long fullDur;
            long.TryParse(command, out fullDur);
            _duration = TimeSpan.FromMilliseconds(fullDur / 1000);
            Dispatcher.Invoke(() => SldrSpan.Maximum = _duration.TotalSeconds);
        }

        private void HandleFullscreen(string command)
        {
            Dispatcher.Invoke(() =>
            {
                bool fs;
                bool.TryParse(command, out fs);
                _isFullscreen = fs;
                if (_isFullscreen)
                {
                    BtnFullscreen.ToolTip = "Exit Fullscreen";
                    ImgFullscreen.Source = new BitmapImage(new Uri("pack://application:,,,/MPDN_RemoteControl;component/Icons/LeaveFullscreen.png"));
                }
                else
                {
                    BtnFullscreen.ToolTip = "Go Fullscreen";
                    ImgFullscreen.Source = new BitmapImage(new Uri("pack://application:,,,/MPDN_RemoteControl;component/Icons/FullScreen.png"));
                }
            });
        }

        private void HandleMute(string command)
        {
            Dispatcher.Invoke(() =>
            {
                bool muted = false;
                Boolean.TryParse(command, out muted);
                _muted = muted;
                if (muted)
                {
                    BtnMute.ToolTip = "Unmute";
                    ImgMute.Source = new BitmapImage(new Uri("pack://application:,,,/MPDN_RemoteControl;component/Icons/UnMute.png"));
                }
                else
                {
                    BtnMute.ToolTip = "Mute";
                    ImgMute.Source = new BitmapImage(new Uri("pack://application:,,,/MPDN_RemoteControl;component/Icons/Mute.png"));
                }
            });
        }

        private void HandleVolume(string command)
        {
            Dispatcher.Invoke(() =>
            {
                int vol = -1;
                int.TryParse(command, out vol);
                if (vol >= 0)
                {
                    SldrVolume.ValueChanged -= sldrVolume_ValueChanged;
                    SldrVolume.Value = vol;
                    LblLevel.Content = vol;
                    SldrVolume.ValueChanged += sldrVolume_ValueChanged;
                }
            });
        }

        private void DisplayVideoTracks(string videoTracks)
        {
            try
            {
                lock(_videoLock)
                {
                Dispatcher.Invoke(() => ShowVideoTracks.Clear());
                var videoSubstrings = Regex.Split(videoTracks, "]]");
                    foreach (var track in videoSubstrings)
                    {
                        var splitData = Regex.Split(track, ">>");
                        int trackNumber;
                        int.TryParse(splitData[0], out trackNumber);
                        bool isActive;
                        bool.TryParse(splitData[3], out isActive);
                        if (trackNumber > 0)
                        {
                            var tmpVideo = new Video
                            {
                                Description = splitData[1],
                                Type = splitData[2],
                                Active = isActive
                            };
                            Dispatcher.Invoke(() => ShowVideoTracks.Add(tmpVideo));
                        }
                    }
                    UpdateVideoControl();
                }
            }
            catch (Exception)
            {
                throw;
            }
        }

        private void DisplayAudioTracks(string audioTracks)
        {
            try
            {
                Dispatcher.Invoke(() => ShowAudioTracks.Clear());
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
                        Dispatcher.Invoke(() => ShowAudioTracks.Add(tmpAudio));
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
            var currentTrack = ShowAudioTracks.FirstOrDefault(t => t.Active);
            if (currentTrack != null && currentTrack.Description != track)
            {
                currentTrack.Active = false;
                var newTrack = ShowAudioTracks.FirstOrDefault(t => t.Description == track);
                if (newTrack != null)
                    newTrack.Active = true;
                UpdateAudioControl();
            }
        }

        private void ChangeActiveVideoTrack(string track)
        {
            var currentTrack = ShowVideoTracks.FirstOrDefault(t => t.Active);
            if (currentTrack == null || currentTrack.Description == track) return;
            currentTrack.Active = false;
            var newTrack = ShowVideoTracks.FirstOrDefault(t => t.Description == track);
            if (newTrack != null)
                newTrack.Active = true;
            UpdateVideoControl();
        }

        private void UpdateAudioControl()
        {
            Dispatcher.Invoke(() =>
            {
                if (ShowAudioTracks.Count > 0)
                {
                    CbAudio.IsEnabled = true;
                    CbAudio.SelectionChanged -= cbAudio_SelectionChanged;
                    CbAudio.SelectedItem = ShowAudioTracks.FirstOrDefault(t => t.Active);
                    CbAudio.SelectionChanged += cbAudio_SelectionChanged;
                }
                else
                    CbAudio.IsEnabled = false;

            });
        }


        private void UpdateVideoControl()
        {
            Dispatcher.Invoke(() =>
            {
                if (ShowVideoTracks.Count > 0)
                {
                    CbVideo.IsEnabled = true;
                    CbVideo.SelectionChanged -= CbVideo_SelectionChanged;
                    CbVideo.SelectedItem = ShowVideoTracks.FirstOrDefault(t => t.Active);
                    CbVideo.SelectionChanged += CbVideo_SelectionChanged;
                }
                else
                    CbVideo.IsEnabled = false;
            });
        }

        private void ChangeActiveSubtitles(string subDesc)
        {
            var currentSub = ShowSubtitles.FirstOrDefault(t => t.ActiveSub);
            if(currentSub != null && currentSub.SubtitleDesc != subDesc)
            {
                currentSub.ActiveSub = false;
                var newSubs = ShowSubtitles.FirstOrDefault(t => t.SubtitleDesc == subDesc);
                if(newSubs != null)
                    newSubs.ActiveSub = true;
                UpdateSubControl();
            }

        }

        private void UpdateSubControl()
        {
            Dispatcher.Invoke(() =>
            {
                if (ShowSubtitles.Count > 0)
                {
                    CbSubtitles.IsEnabled = true;
                    CbSubtitles.SelectionChanged -= cbSubtitles_SelectionChanged;
                    CbSubtitles.SelectedItem = ShowSubtitles.FirstOrDefault(t => t.ActiveSub);
                    CbSubtitles.SelectionChanged += cbSubtitles_SelectionChanged;
                }
                else
                    CbSubtitles.IsEnabled = false;
            });
        }

        private void DisplaySubtitles(string subs)
        {
            try
            {
                Dispatcher.Invoke(() => ShowSubtitles.Clear());
                var subStrings = Regex.Split(subs, "]]");
                if (subStrings.Count() > 1)
                {
                    foreach (var sub in subStrings)
                    {
                        var splitData = Regex.Split(sub, ">>");

                        int subNumber = -1;
                        int.TryParse(splitData[0], out subNumber);
                        bool isActive = false;
                        Boolean.TryParse(splitData[3], out isActive);
                        if (subNumber > 0)
                        {
                            Subtitles tmpSub = new Subtitles()
                            {
                                SubtitleDesc = splitData[1],
                                SubtitleType = splitData[2],
                                ActiveSub = isActive
                            };
                            Dispatcher.Invoke(() => ShowSubtitles.Add(tmpSub));
                        }
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
                Dispatcher.Invoke(() => ShowChapters.Clear());
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
                        Dispatcher.Invoke(() => ShowChapters.Add(tmpChapter));
                    }
                }
                Dispatcher.Invoke(() =>
                {
                    if (ShowChapters.Count > 0)
                    {
                        var currChapter = ShowChapters.FirstOrDefault(t => t.ChapterLocation >= _currenLocation);
                        if (currChapter != null && CbChapters.SelectedIndex != currChapter.ChapterIndex)
                        {
                            CbChapters.SelectionChanged -= cbChapters_SelectionChanged;
                            CbChapters.SelectedIndex = (currChapter.ChapterIndex - 2);
                            CbChapters.SelectionChanged += cbChapters_SelectionChanged;
                        }
                        CbChapters.IsEnabled = true;
                    }
                    else
                        CbChapters.IsEnabled = false;
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
            if(_playState == "Stopped" || _playState == "Paused" || _playState == "Disconnected")
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
            CbChapters.SelectionChanged -= cbChapters_SelectionChanged;
            ShowChapters.Clear();
            CbChapters.SelectionChanged += cbChapters_SelectionChanged;
            CbSubtitles.SelectionChanged -= cbSubtitles_SelectionChanged;
            ShowSubtitles.Clear();
            CbSubtitles.SelectionChanged -= cbSubtitles_SelectionChanged;
            CbAudio.SelectionChanged -= cbAudio_SelectionChanged;
            ShowAudioTracks.Clear();
            ShowVideoTracks.Clear();
            CbAudio.SelectionChanged += cbAudio_SelectionChanged;
            CbChapters.IsEnabled = false;
            CbSubtitles.IsEnabled = false;
            SldrVolume.IsEnabled = false;
            CbAudio.IsEnabled = false;
            CbVideo.IsEnabled = false;
            _duration = new TimeSpan(0,0,0,0);
            _currentFile = string.Empty;
            LblPosition.Content = "00:00:00";
            LblFile.Content = "None";
            LblState.Content = "Not Connected";
            _playlistContent.Clear();

            BtnBrowse.IsEnabled = false;
            BtnAddToPlaylist.IsEnabled = false;
            BtnPrevious.IsEnabled = false;
            BtnNext.IsEnabled = false;
            BtnPlaylistShow.IsEnabled = false;
            BtnUrl.IsEnabled = false;
            BtnClear.IsEnabled = false;
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
            var item = CbChapters.SelectedItem as Chapter;
            if(item != null)
                PassCommandToServer("Seek|" + item.ChapterLocation);
        }

        private void cbSubtitles_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var sub = CbSubtitles.SelectedItem as Subtitles;
            if(sub != null)
                PassCommandToServer("ActiveSubTrack|" + sub.SubtitleDesc);
        }

        private void cbAudio_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var aud = CbAudio.SelectedItem as Audio;
            if(aud != null)
                PassCommandToServer("ActiveAudioTrack|" + aud.Description);
        }

        private void CbVideo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var vid = CbVideo.SelectedItem as Video;
            if(vid != null)
                PassCommandToServer("ActiveVideoTrack|" + vid.Description);
        }

        private void BtnAddToPlaylist_Click(object sender, RoutedEventArgs e)
        {
            var openFile = new OpenFileDialog
            {
                Multiselect = true,
                Title = "Select File(s) to Play"
            };
            var showDialog = openFile.ShowDialog();
            if (showDialog == null || !(bool) showDialog) return;
            var files = openFile.FileNames;
            var sb = new StringBuilder();
            var counter = 1;
            foreach (var file in files)
            {
                if (counter > 1)
                    sb.Append(">>");
                sb.Append(file);
                counter++;
            }
            PassCommandToServer("AddFilesToPlaylist|" + sb);
            BtnPlayPause.IsEnabled = true;
        }

        private void OpenUrl()
        {
            var input = new InputDialog("Add URL to the playlist", "Enter the URL you'd like to add to the playlist");
            var result = input.ShowDialog();
            if (result != true || string.IsNullOrEmpty(input.Response)) return;
            var urlToAdd = input.Response;
            PassCommandToServer($"AddFilesToPlaylist|{urlToAdd}");
        }

        private void BtnPrevious_Click(object sender, RoutedEventArgs e)
        {
            PassCommandToServer("PlayPrevious|"); 
        }

        private void BtnNext_Click(object sender, RoutedEventArgs e)
        {
            PassCommandToServer("PlayNext|");
        }

        private void Button_Click_1(object sender, RoutedEventArgs e)
        {
            if(BtnPlaylistShow.ToolTip.ToString() == "Show")
                PassCommandToServer("ShowPlaylist|");
            else
            {
                PassCommandToServer("HidePlaylist|");
            }
        }

        private void DataGridPlaylist_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var itemIndex = DataGridPlaylist.SelectedIndex;
            PassCommandToServer("PlaySelectedFile|" + itemIndex);
        }

        private void DataGridPlaylist_KeyUp(object sender, KeyEventArgs e)
        {
            if(e.Key == Key.Delete && DataGridPlaylist.SelectedIndex != -1)
            {
                PassCommandToServer("RemoveFile|" + DataGridPlaylist.SelectedIndex);
            }
        }

        public new void DragOver(IDropInfo dropInfo)
        {
            if (dropInfo.Data != null && dropInfo.TargetItem != null)
            {
                var sourceItem = (KeyValuePair<string, bool>) dropInfo.Data;
                var targetItem = (KeyValuePair<string, bool>) dropInfo.TargetItem;

                if (sourceItem.Key != targetItem.Key)
                {
                    dropInfo.DropTargetAdorner = DropTargetAdorners.Insert;
                    dropInfo.Effects = DragDropEffects.Move;
                }
            }
        }

        public new void Drop(IDropInfo dropInfo)
        {
            if (dropInfo.Data != null && dropInfo.TargetItem != null)
            {
                var sourceItem = (KeyValuePair<string, bool>) dropInfo.Data;
                var targetItem = (KeyValuePair<string, bool>) dropInfo.TargetItem;

                //playlistContent.Remove(sourceItem);

                var idx = _playlistContent.IndexOf(sourceItem);
                _playlistContent.RemoveAt(idx);
                PassCommandToServer("RemoveFile|" + idx);
                var insertIdx = dropInfo.InsertIndex;
                if (dropInfo.InsertPosition == RelativeInsertPosition.AfterTargetItem)
                    insertIdx--;
                if (insertIdx > _playlistContent.Count)
                    insertIdx = _playlistContent.Count - 1;
                _playlistContent.Insert(insertIdx, sourceItem);

                PassCommandToServer("InsertFileInPlaylist|" + insertIdx + "|" + sourceItem.Key);
            }
        }

        private void BtnAbout_Click(object sender, RoutedEventArgs e)
        {
            AboutWindow myAbout = new AboutWindow();
            myAbout.ShowDialog();
        }

        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            PassCommandToServer("ClearPlaylist|");
        }

        private void BtnUrl_Click(object sender, RoutedEventArgs e)
        {
            OpenUrl();
        }
    }
}
