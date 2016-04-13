/*
    _                _      _  ____   _                           _____
   / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
  / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
 / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
/_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|

 Copyright 2015-2016 Florian "KlappPC" Lang
 Contact: ichhoeremusik@gmx.net

 Licensed under the Apache License, Version 2.0 (the "License");
 you may not use this file except in compliance with the License.
 You may obtain a copy of the License at

 http://www.apache.org/licenses/LICENSE-2.0
					
 Unless required by applicable law or agreed to in writing, software
 distributed under the License is distributed on an "AS IS" BASIS,
 WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 See the License for the specific language governing permissions and
 limitations under the License.

*/

using System;
using System.Drawing;
using System.Windows.Forms;
using System.ServiceModel;

namespace Gui2
{

    public partial class Form1 : Form
    {
        private bool safeMode = false;
        private bool sendAll = false;
        private bool sendAny = true;
        private bool sendOne = false;
        private string botName = "";
        string[] botList;
        private string URL = "";
        ServerProcess proc;
        private Client Client;
        public Form1()
        {
            InitializeComponent();

            // So either the ASF.exe is in the same directory, or we assume development environment.
            if (System.IO.File.Exists("ASF.exe"))
            {
                proc = new ServerProcess("ASF.exe", "--server", textBox2);
            }
            else
            {
                proc = new ServerProcess("../../../ArchiSteamFarm/bin/Release/ArchiSteamFarm.exe", "--server", textBox2);
            }         
            proc.Start();
        }


        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            proc.Stop();
            base.OnFormClosing(e);
        }

        /**
         * Sends a single command. can be lead by a ! but it does not have to.
         */
        private string sendCommand(string command)
        {
            if (command.StartsWith("!"))
            {
                command=command.Substring(1);
            }
            if (Client == null)
            {
                Client = new Client(new BasicHttpBinding(), new EndpointAddress(URL));
            }
            return Client.HandleCommand(command);
        } 

        /**
         * Maximize again when double clicked on tray icon
         */
        private void ASFGUI_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            this.Show();
            this.WindowState = FormWindowState.Normal;
        }

        private void Form1_Load(object sender, System.EventArgs e)
        {
            this.Resize += new System.EventHandler(this.Form1_Resize);
            textBox2.ScrollBars = ScrollBars.Vertical;
            textBox1.ScrollBars = ScrollBars.Vertical;
            checkBox4.Checked = true;
            textBox3.Text = "http://localhost:1242/ASF";
            URL = "http://localhost:1242/ASF";
            textBox2.Anchor = (AnchorStyles.Right | AnchorStyles.Left);
        }
        /**
         * Minimize to tray instead of taskbar
         */
        private void Form1_Resize(object sender, EventArgs e)
        {
            if (FormWindowState.Minimized == this.WindowState)
            {
                ASFGUI.Visible = true;
                this.Hide();
            }
            else if (FormWindowState.Normal == this.WindowState)
            {
                ASFGUI.Visible = false;
            }
        }

        /**
         * generate a command from a simple command
         * That means, adds a botName or makes multiple commands for multiple bots.
         */
        private string generateCommand(string command, string arg = "")
        {
            if (sendOne)
                return command + " " + botName + " " + arg;
            if (sendAll)
            {
                string ret = "";
                foreach (string str in botList)
                {
                    ret = ret + command + " " + str + " " + arg + "\r\n";
                }
                return ret;
            }
            return command + arg;
        }
        /**
         * One of the simple buttons got pressed
         */
        private void buttonPressed(string command)
        {
            textBox1.Text = generateCommand(command);
            if (!safeMode)
                button1_Click(this, null);
        }
        /**
         * One of the complicated buttons was pressed
         * We get an argumentlist 
         */
        private void multiCommand(string command)
        {
            if (sendAll)
                return;
            string[] arr = textBox1.Lines;
            string cmd = "";
            for (int i = 0; i < arr.Length; i++)
            {
                if (!String.IsNullOrEmpty(arr[i].Trim()))
                {
                    cmd = cmd + generateCommand(command, arr[i].Trim()) + "\r\n";
                }
            }
            textBox1.Text = cmd;
        }

        /**
         * updates the WCF URL in case of custom URL
         */
        private void textBox3_TextChanged(object sender, EventArgs e)
        {
            URL = textBox3.Text;
        }

        private void comboBox1_SelectedIndexChanged(object sender, EventArgs e)
        {
            botName= comboBox1.SelectedItem.ToString();
        }
        //Ok, radiobuttons would have been better I guess, to lazy to change now.
        private void checkBox3_CheckedChanged(object sender, EventArgs e)
        {
            //specific
            if (checkBox3.Checked)
            {
                sendAll = false;
                sendAny = false;
                sendOne = true;
                checkBox2.Checked = false;
                checkBox4.Checked = false;
            }
        }

        private void checkBox2_CheckedChanged(object sender, EventArgs e)
        {
            //all
            if (checkBox2.Checked)
            {
                sendAll = true;
                sendAny = false;
                sendOne = false;
                checkBox3.Checked = false;
                checkBox4.Checked = false;
            }
        }

        private void checkBox4_CheckedChanged(object sender, EventArgs e)
        {
            //any
            if (checkBox4.Checked)
            {
                sendAll = false;
                sendAny = true;
                sendOne = false;
                checkBox2.Checked = false;
                checkBox3.Checked = false;
            }
        }

        private void checkBox1_CheckedChanged(object sender, EventArgs e)
        {
            safeMode = checkBox1.Checked;
        }


        /**
         * Send command button.
         */
        private void button1_Click(object sender, System.EventArgs e)
        {
            for (int i = 0; i < textBox1.Lines.Length; i++)
            {
                string command = textBox1.Lines[i];
                if (!String.IsNullOrEmpty(command.Trim()))
                {
                    sendCommand(command);
                }
            }

        }
        /**
         * Update /Generate Botlist button
         */
        private void button6_Click(object sender, EventArgs e)
        {
            string ret = sendCommand("statusall");
            string[] arr = ret.Split('\n');
            int botAmount = Convert.ToInt16(arr[arr.Length - 1].Split('/')[1].Trim().Split(' ')[0]);
            botList = new string[botAmount];
            for (int i = 0; i < botAmount; i++)
            {
                botList[i] = arr[arr.Length - 2 - i].Substring(3).Trim().Split(' ')[0];
            }
            comboBox1.Items.AddRange(botList);
        }

        //The Rest are simple buttons.
        private void button3_Click(object sender, EventArgs e)
        {
            multiCommand("redeem");
        }
        private void button2_Click(object sender, EventArgs e)
        {
            textBox1.Text = generateCommand("loot");
            if (!safeMode)
                button1_Click(this, null);
        }
        private void button4_Click(object sender, EventArgs e)
        {
            //2fa 
            textBox1.Text = generateCommand("2fa");
            if (!safeMode)
                button1_Click(this, null);
        }
        private void button5_Click(object sender, EventArgs e)
        {
            buttonPressed("2faok");
        }
        private void button7_Click(object sender, EventArgs e)
        {
            buttonPressed("2fano");
        }
        private void button8_Click(object sender, EventArgs e)
        {
            if (sendAll)
                return;
            textBox1.Text = generateCommand("2faoff");
        }
        private void button9_Click(object sender, EventArgs e)
        {
            textBox1.Text = "exit";
        }
        private void button10_Click(object sender, EventArgs e)
        {
            buttonPressed("farm");
        }
        private void button11_Click(object sender, EventArgs e)
        {
            buttonPressed("help");
        }
        private void button12_Click(object sender, EventArgs e)
        {
            buttonPressed("start");
        }
        private void button13_Click(object sender, EventArgs e)
        {
            buttonPressed("stop");
        }
        private void button14_Click(object sender, EventArgs e)
        {
            buttonPressed("pause");
        }
        private void button15_Click(object sender, EventArgs e)
        {
            buttonPressed("status");
        }
        private void button16_Click(object sender, EventArgs e)
        {
            textBox1.Text = "statusall";
            if (!safeMode)
                button1_Click(this, null);
        }
        private void button17_Click(object sender, EventArgs e)
        {
            multiCommand("owns");
        }
        private void button18_Click(object sender, EventArgs e)
        {
            multiCommand("addlicense");
        }
        private void button19_Click(object sender, EventArgs e)
        {
            multiCommand("play");
        }
        private void button20_Click(object sender, EventArgs e)
        {
            textBox1.Text = "";
        }
    }

    //############### After this point copied from Archie's WCF ###################
    [ServiceContract]
    interface IWCF
    {
        [OperationContract]
        string HandleCommand(string input);
    }
    class Client : ClientBase<IWCF>, IWCF
    {
        internal Client(System.ServiceModel.Channels.Binding binding, EndpointAddress address) : base(binding, address) { }

        public string HandleCommand(string input)
        {
            try
            {
                return Channel.HandleCommand(input);
            }
            catch (Exception e)
            {
                //Logging.LogGenericException(e);
                return null;
            }
        }
    }
}
