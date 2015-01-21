using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace MPDN_RemoteControl
{
    public class ClientGuid
    {
        #region Variables
        private Guid _myGuid;
        private string _filePath = "myguid.conf";
        private Guid _nullGuid = Guid.Parse("{00000000-0000-0000-0000-000000000000}");
        #endregion

        #region Properties
        public Guid GetGuid
        {
            get
            {
                GetMyGuid();
                return _myGuid;
            }
        }
        #endregion

        private void CreateMyGuid()
        {
            try
            {
                if (!File.Exists(_filePath))
                {
                    _myGuid = Guid.NewGuid();
                    var file = File.Open(_filePath, FileMode.OpenOrCreate, FileAccess.ReadWrite);
                    StreamWriter writer = new StreamWriter(file);
                    writer.WriteLine(_myGuid);
                    writer.Flush();
                    writer.Close();
                }
            }
            catch(Exception ex)
            {
                MessageBox.Show("Error occured saving ID to file", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private string ReadMyGuid()
        {
            string fileGuid = String.Empty;
            if(File.Exists(_filePath))
            {
                var file = File.Open(_filePath, FileMode.Open, FileAccess.Read);
                StreamReader reader = new StreamReader(file);
                fileGuid = reader.ReadLine();
                reader.Close();
            }

            return fileGuid;
        }

        private void GetMyGuid()
        {
            if (_myGuid == _nullGuid)
            {
                //First try to read the guid from the file
                string readGuid = ReadMyGuid();
                if(String.IsNullOrEmpty(readGuid))
                {
                    CreateMyGuid();
                }
                else
                {
                    Guid.TryParse(readGuid, out _myGuid);
                }
            }
        }
    }
}
