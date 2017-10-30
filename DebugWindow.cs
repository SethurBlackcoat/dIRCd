using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows.Forms;
using Newtonsoft.Json;

namespace dIRCd
{
	public partial class DebugWindow : Form
	{
		private BridgeServer bridge;
		private BridgeConfig? config = null;
		private string configPath = Directory.GetCurrentDirectory() + Path.DirectorySeparatorChar + "config.json";
		private JsonSerializerSettings serializerSettings = new JsonSerializerSettings()
		{
			Formatting = Formatting.Indented
		};

		public DebugWindow()
		{
			InitializeComponent();
			try
			{
				config = JsonConvert.DeserializeObject<BridgeConfig>(File.ReadAllText(configPath, Encoding.UTF8));
			}
			catch (Exception e)
			{
				WriteOut(e.ToString() + ": " + e.Message + "\n" + e.StackTrace);
			}
		}

		private void DebugWindow_Resize(object sender, EventArgs e)
		{
			if (this.WindowState == FormWindowState.Minimized)
			{
				this.Hide();
			}
		}

		private void TrayIcon_MouseDoubleClick(object sender, MouseEventArgs e)
		{
			this.Show();
			this.WindowState = FormWindowState.Normal;
		}

		private void Button1_Click(object sender, EventArgs e)
		{
			if (config != null)
			{
				if (bridge == null)
				{
					bridge = new BridgeServer(WriteOut, config.Value);
					bridge.Run();
					Button1.Text = "stop";
				}
				else
				{
					bridge.Shutdown();
					bridge = null;
					Button1.Text = "start";
				}
			}
			else
			{
				WriteOut("Can't start with invalid config - check error above!");
			}
		}

		public void WriteOut(string message)
		{
			output.BeginInvoke(new MethodInvoker(delegate { output.AppendText(message + Environment.NewLine); output.ScrollToCaret(); }));
		}

		private void DebugWindow_Shown(object sender, EventArgs e)
		{
			Button1_Click(this, EventArgs.Empty);
			LogLevelSelector.SelectedIndex = (int) bridge.bridgeConfig.logLevel;
		}

		private void LogLevelSelector_SelectionChangeCommitted(object sender, EventArgs e)
		{
			bridge.bridgeConfig.logLevel = (Discord.LogSeverity) LogLevelSelector.SelectedIndex;
		}
	}
}
