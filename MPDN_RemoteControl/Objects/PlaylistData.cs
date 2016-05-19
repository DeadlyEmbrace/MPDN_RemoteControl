using System.Collections.Generic;

namespace MPDN_RemoteControl.Objects
{
    public class PlaylistData
    {
        public string PlaylistName { get; set; }
        public List<PlaylistItem> Playlist { get; set; }
    }
}