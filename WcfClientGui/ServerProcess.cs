/*
    _                _      _  ____   _                           _____
   / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
  / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
 / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
/_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|

 Copyright 2015-2016 Florian "KlappPC" Lang
 Contact: ichhoeremusik@gmx.net
 This file is mostly done by a friend who explicitly does not want to get mentioned in any way.

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

using System.Threading;
using System.Diagnostics;
using System.Windows.Forms;

namespace Gui2
{
    /*basically a class to run executables as controlled prozess in the background*/
    public class ServerProcess
    {
        //ASF.exe in our case
        protected Process process;
        //handling the output.
        protected Thread outputThread;
        protected bool stopping;

        //the textbox from our Form, where we want to display output.
        private TextBox output;

        private object lockObj = new object();

        /**
         * New SeverProcess for filename with arguments and output to textBox.
         * Console is hidden and IO redirected.
         */
        public ServerProcess(string fileName, string argumants,TextBox textBox){


            output = textBox;
            process = new System.Diagnostics.Process();

            process.StartInfo.FileName = fileName;
            process.StartInfo.Arguments = argumants;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardInput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.CreateNoWindow = true;

        }
        //needed for realizing when input is needed.
        private int dotcounter = 0;

        /**
         * I'm not quite happy with this. I could not figure a way to notice when input is required 
         * besides reading char by char and searching for keywords. Will stop working, if the "Please enter"
         * lines gets changed.
         * Only tested for "Enter Password."
         */
        private void NewOutput(object sender, char e)
        {
            MethodInvoker mi = delegate
            {
                output.AppendText(e.ToString());
            };

            if (e == '.')
            {
                dotcounter++;
            }
            else if(e == ':') {
                dotcounter = 3;
            }else{
                dotcounter = 0;
            }
            if (dotcounter == 3) {
            string[] arr = output.Lines;
            string str = arr[arr.Length - 1];
                if (str.Contains("Hit enter"))
                {
                    str = arr[arr.Length - 2] + " | " + str;
                    Form f = new Form2(this, str);
                    f.ShowDialog();
                    mi = delegate{output.AppendText(e.ToString()+"\n");};
                }
                if (str.Contains("Please enter"))
                {
                    Form f = new Form2(this, str);
                    f.ShowDialog();
                    mi = delegate { output.AppendText(e.ToString() + "\n"); };
                }

                dotcounter = 0;
            }
            output.Invoke(mi);

        }

        private void NewOutput(object sender, string e)
        {
            MethodInvoker mi = delegate
            {
                output.AppendText(e+"\n");
            };
            output.Invoke(mi);

        }


        private void printOutPut() {
            char str;
            int i;
            string s;
            while (!stopping)
            {
                //thats ugly, but when using readline we can't catch input.
                while (((i = process.StandardOutput.Read()) != 0))
                {
                   str=System.Convert.ToChar(i);
                   NewOutput(this, str);
                    if (stopping)
                        break;
                }
                
                while (((s = process.StandardError.ReadLine()) != null))
                {
                    NewOutput(this, s);
                    if (stopping)
                        break;
                }
            }
        }

        public void Write(string msg) {
            process.StandardInput.WriteLine(msg);
            process.StandardInput.Flush();
        }

        public void Stop() {
            Thread stopThread = new Thread(StopProcess);
            stopThread.Start();
        }

        private void StopProcess() {

            if (process == null)
                return;
            stopping = true;

            outputThread.Abort();

            Thread.Sleep(1000);

            if (process == null)
                return;

            if (process.HasExited)
                process.Close();
            else
                process.Kill();

            process = null;
        }
        /**
         * starts the process and a second thread to listen for output.
         */
        public void Start() {
            outputThread = new Thread(printOutPut);
            process.Start();
            outputThread.Start();
        }

        public Process Process {
            get {
                return process;
            }
        }
    }
}
