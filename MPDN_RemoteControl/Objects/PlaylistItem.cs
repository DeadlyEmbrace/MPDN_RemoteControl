using System;
using System.Collections.Generic;
using System.IO;

namespace MPDN_RemoteControl.Objects
{
    public class PlaylistItem
    {
        public string FilePath { get; set; }
        public bool Active { get; set; }
        public bool HasChapter { get; set; }
        public List<int> SkipChapters { get; set; }
        public int EndChapter { get; set; }
        public string Duration { get; set; }
        public int PlayCount { get; set; }
        public Guid Guid { get; set; }

        public PlaylistItem()
        {
        }

        public PlaylistItem(string filePath, bool isActive)
        {
            if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentNullException("filePath");

            FilePath = filePath;
            Active = isActive;
            PlayCount = 0;
            Guid = Guid.NewGuid();
        }

        public PlaylistItem(string filePath, List<int> skipChapter, int endChapter, bool isActive)
        {
            if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentNullException("filePath");

            FilePath = filePath;
            Active = isActive;
            SkipChapters = skipChapter;
            EndChapter = endChapter;
            HasChapter = true;
            PlayCount = 0;
            Guid = Guid.NewGuid();
        }

        public PlaylistItem(string filePath, List<int> skipChapter, int endChapter, bool isActive, string duration)
        {
            if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentNullException("filePath");

            FilePath = filePath;
            Active = isActive;
            SkipChapters = skipChapter;
            EndChapter = endChapter;
            HasChapter = true;
            Duration = duration;
            PlayCount = 0;
            Guid = Guid.NewGuid();
        }

        public PlaylistItem(string filePath, List<int> skipChapter, int endChapter, bool isActive, string duration,
            int playCount)
        {
            if (string.IsNullOrWhiteSpace(filePath)) throw new ArgumentNullException("filePath");

            FilePath = filePath;
            Active = isActive;
            SkipChapters = skipChapter;
            EndChapter = endChapter;
            HasChapter = true;
            Duration = duration;
            PlayCount = playCount;
            Guid = Guid.NewGuid();
        }

        public override string ToString()
        {
            if (HasChapter)
            {
                return Path.GetFileName(FilePath) + " | SkipChapter: " + string.Join(",", SkipChapters) +
                       " | EndChapter: " + EndChapter;
            }

            return Path.GetFileName(FilePath) ?? "???";
        }
    }
}