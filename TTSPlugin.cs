using System;
using System.Speech.Synthesis;
using System.Reflection;
using System.Text.RegularExpressions; 
using Decal.Adapter;
using Decal.Adapter.Wrappers;

[assembly: AssemblyVersion("0.1.4.0")] 

namespace ACTextToSpeech
{
    [WireUpBaseEvents]
    [FriendlyName("AC Text To Speech")]
    public class TTSPlugin : PluginBase
    {
        private SpeechSynthesizer synth;
        private bool isEnabled = true;

        // Variables to track who is talking and when
        private string lastSpeaker = "";
        private DateTime lastTellTime = DateTime.MinValue;

        protected override void Startup()
        {
            try
            {
                synth = new SpeechSynthesizer();
                synth.SetOutputToDefaultAudioDevice();
                
                CoreManager.Current.Actions.AddChatText("[TTS Plugin] v0.1.4.0 Online! Type /tts off or /tts on to toggle.", 5);
                synth.SpeakAsync("Text to speech online.");

                CoreManager.Current.ChatBoxMessage += OnChatBoxMessage;
                CoreManager.Current.CommandLineText += OnCommandLineText; 
            }
            catch (Exception ex) { CoreManager.Current.Actions.AddChatText(ex.ToString(), 5); }
        }

        protected override void Shutdown()
        {
            try
            {
                CoreManager.Current.ChatBoxMessage -= OnChatBoxMessage;
                CoreManager.Current.CommandLineText -= OnCommandLineText;
                if (synth != null) synth.Dispose();
            }
            catch { }
        }

        private void OnCommandLineText(object sender, ChatParserInterceptEventArgs e)
        {
            try
            {
                string text = e.Text.ToLower();
                
                if (text == "/tts off")
                {
                    isEnabled = false;
                    CoreManager.Current.Actions.AddChatText("[TTS Plugin] Speech Disabled.", 5);
                    synth.SpeakAsync("Speech disabled.");
                    e.Eat = true;
                }
                else if (text == "/tts on")
                {
                    isEnabled = true;
                    CoreManager.Current.Actions.AddChatText("[TTS Plugin] Speech Enabled.", 5);
                    synth.SpeakAsync("Speech enabled.");
                    e.Eat = true;
                }
            }
            catch { }
        }

        private void OnChatBoxMessage(object sender, ChatTextInterceptEventArgs e)
        {
            try
            {
                if (!isEnabled) return;

                string cleanText = Regex.Replace(e.Text, "<.*?>", string.Empty);
                string lowerText = cleanText.ToLower();

                bool isTell = lowerText.Contains(" tells you, ");
                bool isChannel = lowerText.StartsWith("[general]") || 
                                 lowerText.StartsWith("[trade]") || 
                                 lowerText.StartsWith("[lfg]") || 
                                 lowerText.StartsWith("[allegiance]") || 
                                 lowerText.StartsWith("[fellowship]") || 
                                 lowerText.StartsWith("[patron]") || 
                                 lowerText.StartsWith("[vassals]") ||
                                 lowerText.StartsWith("[society]");

                if (isTell)
                {
                    // Find exactly where the name ends and the message begins
                    int tellIndex = cleanText.IndexOf(" tells you, ", StringComparison.OrdinalIgnoreCase);
                    
                    if (tellIndex > 0)
                    {
                        // Split the speaker's name from the message itself
                        string speaker = cleanText.Substring(0, tellIndex);
                        string message = cleanText.Substring(tellIndex + " tells you, ".Length);

                        // If it's the exact same speaker talking within 15 seconds, just read the message
                        if (speaker == lastSpeaker && (DateTime.Now - lastTellTime).TotalSeconds < 15)
                        {
                            synth.SpeakAsync(message);
                        }
                        else
                        {
                            // If it's a new speaker or it's been a while, read the whole line
                            synth.SpeakAsync(cleanText);
                        }

                        // Update our memory with this speaker's name and the current time
                        lastSpeaker = speaker;
                        lastTellTime = DateTime.Now;
                    }
                    else 
                    {
                        // Fallback just in case
                        synth.SpeakAsync(cleanText);
                    }
                }
                else if (isChannel)
                {
                    synth.SpeakAsync(cleanText);
                    
                    // If a channel message interrupts, reset the memory so the next tell announces the name again
                    lastSpeaker = ""; 
                }
            }
            catch { }
        }
    }
}