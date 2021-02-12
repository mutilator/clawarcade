using System;
using System.Drawing;
using System.Windows.Forms;

namespace InternetClawMachine
{
    #region InputBox return result

    /// <summary>
    /// Class used to store the result of an InputBox.Show message.
    /// </summary>
    public class InputBoxResult
    {
        public DialogResult ReturnCode { get; set; }
        public string Text { get; set; }
    }

    #endregion InputBox return result

    /// <summary>
    /// Summary description for InputBox.
    /// </summary>
    public static class InputBox
    {
        #region Private Windows Contols and Constructor

        // Create a new instance of the form.
        private static Form _frmInputDialog;

        private static Label _lblPrompt;
        private static Button _btnOk;
        private static Button _btnCancel;
        private static TextBox _txtInput;

        #endregion Private Windows Contols and Constructor

        #region Private Variables

        private static string _formCaption = string.Empty;
        private static string _formPrompt = string.Empty;
        private static InputBoxResult _outputResponse = new InputBoxResult();
        private static string _defaultValue = string.Empty;
        private static int _xPos = -1;
        private static int _yPos = -1;

        #endregion Private Variables

        #region Windows Form code

        private static void InitializeComponent()
        {
            // Create a new instance of the form.
            _frmInputDialog = new Form();
            _lblPrompt = new Label();
            _btnOk = new Button();
            _btnCancel = new Button();
            _txtInput = new TextBox();
            _frmInputDialog.SuspendLayout();
            //
            // lblPrompt
            //
            _lblPrompt.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            _lblPrompt.BackColor = SystemColors.Control;
            _lblPrompt.Font = new Font("Microsoft Sans Serif", 8.25F, FontStyle.Regular, GraphicsUnit.Point, 0);
            _lblPrompt.Location = new Point(12, 9);
            _lblPrompt.Name = "_lblPrompt";
            _lblPrompt.Size = new Size(302, 82);
            _lblPrompt.TabIndex = 3;
            //
            // btnOK
            //
            _btnOk.DialogResult = DialogResult.OK;
            _btnOk.FlatStyle = FlatStyle.Popup;
            _btnOk.Location = new Point(326, 8);
            _btnOk.Name = "_btnOk";
            _btnOk.Size = new Size(64, 24);
            _btnOk.TabIndex = 1;
            _btnOk.Text = @"&OK";
            _btnOk.Click += btnOK_Click;
            //
            // btnCancel
            //
            _btnCancel.DialogResult = DialogResult.Cancel;
            _btnCancel.FlatStyle = FlatStyle.Popup;
            _btnCancel.Location = new Point(326, 40);
            _btnCancel.Name = "_btnCancel";
            _btnCancel.Size = new Size(64, 24);
            _btnCancel.TabIndex = 2;
            _btnCancel.Text = @"&Cancel";
            _btnCancel.Click += btnCancel_Click;
            //
            // txtInput
            //
            _txtInput.Location = new Point(8, 100);
            _txtInput.Name = "_txtInput";
            _txtInput.Size = new Size(379, 20);
            _txtInput.TabIndex = 0;
            _txtInput.Text = "";
            _txtInput.KeyPress += txtInput_KeyPress;

            //
            // InputBoxDialog
            //
            _frmInputDialog.AutoScaleBaseSize = new Size(5, 13);
            _frmInputDialog.ClientSize = new Size(398, 128);
            _frmInputDialog.Controls.Add(_txtInput);
            _frmInputDialog.Controls.Add(_btnCancel);
            _frmInputDialog.Controls.Add(_btnOk);
            _frmInputDialog.Controls.Add(_lblPrompt);
            _frmInputDialog.FormBorderStyle = FormBorderStyle.FixedDialog;
            _frmInputDialog.MaximizeBox = false;
            _frmInputDialog.MinimizeBox = false;
            _frmInputDialog.Name = "InputBoxDialog";
            _frmInputDialog.Shown += frmInputDialog_Shown;
            _frmInputDialog.ResumeLayout(false);
        }

        private static void frmInputDialog_Shown(object sender, EventArgs e)
        {
            _txtInput.Focus();
        }

        private static void txtInput_KeyPress(object sender, KeyPressEventArgs e)
        {
            if (e.KeyChar == '\r')
            {
                OutputResponse.ReturnCode = DialogResult.OK;
                OutputResponse.Text = _txtInput.Text;
                _frmInputDialog.Close();
            }
        }

        #endregion Windows Form code

        #region Private function, InputBox Form move and change size

        private static void LoadForm()
        {
            OutputResponse.ReturnCode = DialogResult.Ignore;
            OutputResponse.Text = string.Empty;

            _txtInput.Text = _defaultValue;
            _lblPrompt.Text = _formPrompt;
            _frmInputDialog.Text = _formCaption;

            // Retrieve the working rectangle from the Screen class
            // using the PrimaryScreen and the WorkingArea properties.
            var workingRectangle = Screen.PrimaryScreen.WorkingArea;

            if (_xPos >= 0 && _xPos < workingRectangle.Width - 100 && _yPos >= 0 && _yPos < workingRectangle.Height - 100)
            {
                _frmInputDialog.StartPosition = FormStartPosition.Manual;
                _frmInputDialog.Location = new Point(_xPos, _yPos);
            }
            else
                _frmInputDialog.StartPosition = FormStartPosition.CenterScreen;

            var prompText = _lblPrompt.Text;

            var n = 0;
            var index = 0;
            while (prompText.IndexOf("\n", index) > -1)
            {
                index = prompText.IndexOf("\n", index) + 1;
                n++;
            }

            if (n == 0)
                n = 1;

            var txt = _txtInput.Location;
            txt.Y = txt.Y + n * 4;
            _txtInput.Location = txt;
            var form = _frmInputDialog.Size;
            form.Height = form.Height + n * 4;
            _frmInputDialog.Size = form;

            _txtInput.SelectionStart = 0;
            _txtInput.SelectionLength = _txtInput.Text.Length;
            _txtInput.Focus();
        }

        #endregion Private function, InputBox Form move and change size

        #region Button control click event

        private static void btnOK_Click(object sender, EventArgs e)
        {
            OutputResponse.ReturnCode = DialogResult.OK;
            OutputResponse.Text = _txtInput.Text;
            _frmInputDialog.Dispose();
        }

        private static void btnCancel_Click(object sender, EventArgs e)
        {
            OutputResponse.ReturnCode = DialogResult.Cancel;
            OutputResponse.Text = string.Empty; //Clean output response
            _frmInputDialog.Dispose();
        }

        #endregion Button control click event

        #region Public Static Show functions

        public static InputBoxResult Show(string prompt)
        {
            InitializeComponent();
            FormPrompt = prompt;

            // Display the form as a modal dialog box.
            LoadForm();
            _frmInputDialog.ShowDialog();
            return OutputResponse;
        }

        public static InputBoxResult Show(string prompt, string title)
        {
            InitializeComponent();

            FormCaption = title;
            FormPrompt = prompt;

            // Display the form as a modal dialog box.
            LoadForm();
            _frmInputDialog.ShowDialog();
            return OutputResponse;
        }

        public static InputBoxResult Show(string prompt, string title, string @default)
        {
            InitializeComponent();

            FormCaption = title;
            FormPrompt = prompt;
            DefaultValue = @default;

            // Display the form as a modal dialog box.
            LoadForm();
            _frmInputDialog.ShowDialog();
            return OutputResponse;
        }

        public static InputBoxResult Show(string prompt, string title, string @default, int xPos, int yPos)
        {
            InitializeComponent();
            FormCaption = title;
            FormPrompt = prompt;
            DefaultValue = @default;
            XPosition = xPos;
            YPosition = yPos;

            // Display the form as a modal dialog box.
            LoadForm();
            _frmInputDialog.ShowDialog();
            return OutputResponse;
        }

        #endregion Public Static Show functions

        #region Private Properties

        private static string FormCaption
        {
            set => _formCaption = value;
        } // property FormCaption

        private static string FormPrompt
        {
            set => _formPrompt = value;
        } // property FormPrompt

        private static InputBoxResult OutputResponse
        {
            get => _outputResponse;
            set => _outputResponse = value;
        } // property InputResponse

        private static string DefaultValue
        {
            set => _defaultValue = value;
        } // property DefaultValue

        private static int XPosition
        {
            set
            {
                if (value >= 0)
                    _xPos = value;
            }
        } // property XPos

        private static int YPosition
        {
            set
            {
                if (value >= 0)
                    _yPos = value;
            }
        } // property YPos

        #endregion Private Properties
    }
}