namespace MPDN_RemoteControl.Controls
{
    /// <summary>
    /// Interaction logic for InputDialog.xaml
    /// </summary>
    public partial class InputDialog
    {
        #region Properties

        public string DialogTitle { get; set; }
        public string QueryText { get; set; }
        public string Response { get; set; }

        #endregion

        #region Constructor

        public InputDialog(string dialogTitle, string queryText)
        {
            InitializeComponent();
            DataContext = this;

            DialogTitle = dialogTitle;
            QueryText = queryText;
        }

        #endregion

        private void Button_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }
    }
}
