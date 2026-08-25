using System;
using System.Speech.Synthesis;
using Decal.Adapter;
using Decal.Adapter.Wrappers;
using VirindiViewService;
using VirindiViewService.Controls;

namespace ACTextToSpeech
{
    [WireUpBaseEvents]
    [FriendlyName("AC Text to Speech")]
    public class PluginCore : PluginBase
    {
        private HudView view;
        private HudButton btnMaster;
        private HudCheckBox chkDirect, chkFellow, chkVendor, chkGeneral, chkSpam, chkAlliance;

        // Volume and Settings controls
        private HudHSlider sldVolume;
        private HudButton btnSettings;

        private SpeechSynthesizer synth;
        private bool masterAudioOn = true;
        private string lastReadMessage = "";

        private string lastSpeakerName = "";
        private DateTime lastSpeakerTime = DateTime.MinValue;

        protected override void Startup()
        {
            try
            {
                synth = new SpeechSynthesizer();
                synth.SetOutputToDefaultAudioDevice();

                string[] resources = typeof(PluginCore).Assembly.GetManifestResourceNames();
                string xmlName = "";
                foreach (string res in resources)
                {
                    if (res.EndsWith("View.xml")) xmlName = res;
                }

                VirindiViewService.XMLParsers.Decal3XMLParser parser = new VirindiViewService.XMLParsers.Decal3XMLParser();
                parser.ParseFromResource(xmlName, out ViewProperties properties, out ControlGroup controls);

                view = new HudView(properties, controls);
                view.ShowInBar = true;
                view.UserMinimizable = true;
                view.Visible = false;

                btnMaster = (HudButton)view["btnMaster"];
                chkDirect = (HudCheckBox)view["chkDirect"];
                chkFellow = (HudCheckBox)view["chkFellow"];
                chkVendor = (HudCheckBox)view["chkVendor"];
                chkGeneral = (HudCheckBox)view["chkGeneral"];
                chkSpam = (HudCheckBox)view["chkSpam"];
                chkAlliance = (HudCheckBox)view["chkAlliance"];

                // Map controls properly
                sldVolume = (HudHSlider)view["sldVolume"];
                btnSettings = (HudButton)view["btnSettings"];

                // Set initial volume from the UI default (will be overwritten if a save file exists)
                synth.Volume = sldVolume.Position;

                // Subscribe to events
                btnMaster.Hit += BtnMaster_Hit;
                sldVolume.Changed += SldVolume_Changed;
                btnSettings.Hit += BtnSettings_Hit;
                CoreManager.Current.ChatBoxMessage += CoreManager_ChatBoxMessage;

                CoreManager.Current.CharacterFilter.LoginComplete += CharacterFilter_LoginComplete;
            }
            catch (Exception ex)
            {
                try
                {
                    string logPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(typeof(PluginCore).Assembly.Location), "TTS_ErrorLog.txt");
                    System.IO.File.WriteAllText(logPath, ex.ToString());
                }
                catch { }
            }
        }

        protected override void Shutdown()
        {
            try
            {
                if (view != null)
                {
                    btnMaster.Hit -= BtnMaster_Hit;
                    sldVolume.Changed -= SldVolume_Changed;
                    btnSettings.Hit -= BtnSettings_Hit;
                    CoreManager.Current.ChatBoxMessage -= CoreManager_ChatBoxMessage;
                    CoreManager.Current.CharacterFilter.LoginComplete -= CharacterFilter_LoginComplete;
                    view.Dispose();
                    view = null;
                }
                if (synth != null)
                {
                    synth.Dispose();
                }
            }
            catch { }
        }

        private string GetSettingsPath()
        {
            string charName = CoreManager.Current.CharacterFilter.Name;
            string serverName = CoreManager.Current.CharacterFilter.Server;
            string dllFolder = System.IO.Path.GetDirectoryName(typeof(PluginCore).Assembly.Location);
            return System.IO.Path.Combine(dllFolder, $"TTS_{serverName}_{charName}.txt");
        }

        private void SaveSettings()
        {
            try
            {
                // Combines settings into one line, e.g., "True|85"
                string saveData = $"{masterAudioOn}|{sldVolume.Position}";
                System.IO.File.WriteAllText(GetSettingsPath(), saveData);
            }
            catch { }
        }

        private void CharacterFilter_LoginComplete(object sender, EventArgs e)
        {
            try
            {
                string path = GetSettingsPath();
                if (System.IO.File.Exists(path))
                {
                    string savedState = System.IO.File.ReadAllText(path).Trim();
                    string[] parts = savedState.Split('|');

                    // 1. Restore the Master Audio Toggle
                    if (parts.Length > 0 && parts[0] == "False")
                    {
                        masterAudioOn = false;
                        btnMaster.Text = "Master Audio: OFF";
                    }

                    // 2. Restore the Volume Level (if it exists in the save file)
                    if (parts.Length > 1 && int.TryParse(parts[1], out int savedVol))
                    {
                        sldVolume.Position = savedVol;
                        synth.Volume = savedVol;
                    }
                }
            }
            catch { }
        }

        private void BtnMaster_Hit(object sender, EventArgs e)
        {
            masterAudioOn = !masterAudioOn;

            if (masterAudioOn)
            {
                btnMaster.Text = "Master Audio: ON";
                synth.SpeakAsync("Text to speech on.");
            }
            else
            {
                btnMaster.Text = "Master Audio: OFF";
                synth.SpeakAsyncCancelAll();
                synth.SpeakAsync("Text to speech off.");
            }

            SaveSettings();
        }

        private void SldVolume_Changed(int min, int max, int pos)
        {
            if (synth != null)
            {
                synth.Volume = pos;
                SaveSettings();
            }
        }

        private void BtnSettings_Hit(object sender, EventArgs e)
        {
            try
            {
                // Decal is 32-bit, so we need the 32-bit SAPI voice menu to change the correct voices
                string sapiPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.SystemX86), @"Speech\SpeechUX\sapi.cpl");

                if (System.IO.File.Exists(sapiPath))
                {
                    System.Diagnostics.Process.Start("explorer.exe", sapiPath);
                }
                else
                {
                    System.Diagnostics.Process.Start("explorer.exe", "ms-settings:speech");
                }
            }
            catch (Exception ex)
            {
                CoreManager.Current.Actions.AddChatText($"[AC TTS] Could not open settings: {ex.Message}", 5);
            }
        }

        private void CoreManager_ChatBoxMessage(object sender, ChatTextInterceptEventArgs e)
        {
            if (!masterAudioOn) return;

            string msg = e.Text.Trim();
            bool shouldRead = false;

            string lowerMsg = msg.ToLower();
            if (lowerMsg.Contains("channel") && (lowerMsg.Contains("join") || lowerMsg.Contains("left") || lowerMsg.Contains("leaving")))
            {
                return;
            }

            if (msg.StartsWith("[Fellowship]"))
            {
                if (chkFellow.Checked) shouldRead = true;
            }
            else if (msg.StartsWith("[Alliance]") || msg.StartsWith("[Allegiance]"))
            {
                if (chkAlliance.Checked) shouldRead = true;
            }
            else if (msg.StartsWith("["))
            {
                if (chkGeneral.Checked) shouldRead = true;
            }
            else if (msg.Contains(" tells you, ") || msg.StartsWith("You tell "))
            {
                if (chkDirect.Checked || chkVendor.Checked) shouldRead = true;
            }
            else if (msg.Contains(" says, "))
            {
                if (msg.Contains(" says, \""))
                {
                    shouldRead = false;
                }
                else
                {
                    if (chkVendor.Checked) shouldRead = true;
                }
            }

            if (shouldRead)
            {
                string speaker = "";
                string spokenText = msg;
                string phraseToSpeak = msg;

                if (msg.Contains(" tells you, "))
                {
                    int idx = msg.IndexOf(" tells you, ");
                    speaker = msg.Substring(0, idx).Trim();
                    spokenText = msg.Substring(idx + 12).Trim();
                }
                else if (msg.StartsWith("You tell "))
                {
                    int idx = msg.IndexOf(",");
                    if (idx != -1 && msg.Length > idx + 1)
                    {
                        speaker = "You";
                        spokenText = msg.Substring(idx + 1).Trim();
                    }
                }
                else if (msg.Contains(" says, "))
                {
                    int idx = msg.IndexOf(" says, ");
                    speaker = msg.Substring(0, idx).Trim();

                    if (speaker.StartsWith("[Fellowship]"))
                    {
                        speaker = speaker.Replace("[Fellowship]", "").Trim();
                    }
                    else if (speaker.StartsWith("[Alliance]"))
                    {
                        speaker = speaker.Replace("[Alliance]", "").Trim();
                    }
                    else if (speaker.StartsWith("[Allegiance]"))
                    {
                        speaker = speaker.Replace("[Allegiance]", "").Trim();
                    }

                    spokenText = msg.Substring(idx + 7).Trim();
                }

                if (spokenText.StartsWith(";") || spokenText.StartsWith("/") || spokenText.StartsWith("!") || spokenText.StartsWith("@"))
                {
                    return;
                }

                string textWithoutNumbers = spokenText.Replace(" ", "").Replace("-", "").Replace(",", "").Trim();
                if (long.TryParse(textWithoutNumbers, out _) || textWithoutNumbers == "")
                {
                    return;
                }

                if (chkSpam.Checked && msg == lastReadMessage)
                {
                    return;
                }
                lastReadMessage = msg;

                if (!string.IsNullOrEmpty(speaker))
                {
                    if (speaker == lastSpeakerName && (DateTime.UtcNow - lastSpeakerTime).TotalSeconds <= 15)
                    {
                        phraseToSpeak = spokenText;
                    }
                    else
                    {
                        lastSpeakerName = speaker;
                    }
                    lastSpeakerTime = DateTime.UtcNow;
                }
                else
                {
                    lastSpeakerName = "";
                }

                synth.SpeakAsync(phraseToSpeak);
            }
        }
    }
}
