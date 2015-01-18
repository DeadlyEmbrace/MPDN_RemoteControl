using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace MPDN_RemoteControl
{
    public class ClientGUID
    {
        #region Variables
        private Guid myGUID;
        private string filePath = "myguid.conf";
        private Guid nullGUID = Guid.Parse("{00000000-0000-0000-0000-000000000000}");
        #endregion

        #region Properties
        public Guid GetGuid
        {
            get
            {
                GetMyGUID();
                return myGUID;
            }
        }
        #endregion

        private void CreateMyGUID()
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    myGUID = Guid.NewGuid();
                    var file = File.Open(filePath, FileMode.OpenOrCreate, FileAccess.ReadWrite);
                    StreamWriter writer = new StreamWriter(file);
                    writer.WriteLine(myGUID);
                    writer.Flush();
                    writer.Close();
                }
            }
            catch(Exception ex)
            {
                MessageBox.Show("Error occured saving ID to file", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private string ReadMyGUID()
        {
            string fileGUID = String.Empty;
            if(File.Exists(filePath))
            {
                var file = File.Open(filePath, FileMode.Open, FileAccess.Read);
                StreamReader reader = new StreamReader(file);
                fileGUID = reader.ReadLine();
                reader.Close();
            }

            return fileGUID;
        }

        private void GetMyGUID()
        {
            if (myGUID == nullGUID)
            {
                //First try to read the guid from the file
                string readGUID = ReadMyGUID();
                if(String.IsNullOrEmpty(readGUID))
                {
                    CreateMyGUID();
                }
                else
                {
                    Guid.TryParse(readGUID, out myGUID);
                }
            }
        }
    }
}
