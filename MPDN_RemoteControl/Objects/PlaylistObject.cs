using System.ComponentModel;

namespace MPDN_RemoteControl.Objects
{
    public class PlaylistObject : INotifyPropertyChanged
    {
        #region Events

        public event PropertyChangedEventHandler PropertyChanged;

        #endregion

        #region Properties

        public string Filename { get; set; }
        public bool Playing { get; set; }

        #endregion
    }
}