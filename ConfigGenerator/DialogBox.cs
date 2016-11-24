/*
    _                _      _  ____   _                           _____
   / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
  / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
 / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
/_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|

 Copyright 2015-2016 Łukasz "JustArchi" Domeradzki
 Contact: JustArchi@JustArchi.net

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
using ConfigGenerator.Properties;

namespace ConfigGenerator {
	internal static class DialogBox {
		internal static DialogResult InputBox(string title, string promptText, out string value) {
			if (string.IsNullOrEmpty(title) || string.IsNullOrEmpty(promptText)) {
				Logging.LogNullError(nameof(title) + " || " + nameof(promptText));
				value = null;
				return DialogResult.Abort;
			}

			TextBox textBox = new TextBox {
				Anchor = AnchorStyles.Right,
				Bounds = new Rectangle(12, 36, 372, 20),
				Width = 1000
			};

			Button buttonOk = new Button {
				Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
				Bounds = new Rectangle(228, 72, 75, 23),
				DialogResult = DialogResult.OK,
				Text = Resources.OK
			};

			Button buttonCancel = new Button {
				Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
				Bounds = new Rectangle(309, 72, 75, 23),
				DialogResult = DialogResult.Cancel,
				Text = Resources.Cancel
			};

			Label label = new Label {
				AutoSize = true,
				Bounds = new Rectangle(9, 20, 372, 13),
				Text = promptText
			};

			Form form = new Form {
				AcceptButton = buttonOk,
				CancelButton = buttonCancel,
				ClientSize = new Size(Math.Max(300, label.Right + 10), 107),
				Controls = { label, textBox, buttonOk, buttonCancel },
				FormBorderStyle = FormBorderStyle.FixedDialog,
				MinimizeBox = false,
				MaximizeBox = false,
				StartPosition = FormStartPosition.CenterScreen,
				Text = title
			};

			DialogResult dialogResult = form.ShowDialog();
			value = textBox.Text;
			return dialogResult;
		}

		internal static DialogResult YesNoBox(string title, string promptText) {
			if (string.IsNullOrEmpty(title) || string.IsNullOrEmpty(promptText)) {
				Logging.LogNullError(nameof(title) + " || " + nameof(promptText));
				return DialogResult.Abort;
			}

			Button buttonYes = new Button {
				Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
				Bounds = new Rectangle(228, 72, 75, 23),
				DialogResult = DialogResult.Yes,
				Text = Resources.Yes
			};

			Button buttonNo = new Button {
				Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
				Bounds = new Rectangle(309, 72, 75, 23),
				DialogResult = DialogResult.No,
				Text = Resources.No
			};

			Label label = new Label {
				AutoSize = true,
				Bounds = new Rectangle(9, 20, 372, 13),
				Text = promptText
			};

			Form form = new Form {
				AcceptButton = buttonYes,
				CancelButton = buttonNo,
				ClientSize = new Size(Math.Max(300, label.Right + 10), 107),
				Controls = { label, buttonYes, buttonNo },
				FormBorderStyle = FormBorderStyle.FixedDialog,
				MinimizeBox = false,
				MaximizeBox = false,
				StartPosition = FormStartPosition.CenterScreen,
				Text = title
			};

			DialogResult dialogResult = form.ShowDialog();
			return dialogResult;
		}
	}
}