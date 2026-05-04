using Advanced_Combat_Tracker;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using System.Xml;

[assembly: AssemblyTitle("EQ1 English Parser")]
[assembly: AssemblyDescription("Parsing engine for EverQuest 1 (English) log files for ACT")]
[assembly: AssemblyCompany("Community")]
[assembly: AssemblyVersion("1.0.0.0")]

namespace ACT_Plugin
{
	/// <summary>
	/// EverQuest 1 English parsing plugin for Advanced Combat Tracker.
	///
	/// Subscribes to <see cref="FormActMain.BeforeLogLineRead"/> and turns raw EQ1
	/// chat-log lines into <see cref="MasterSwing"/> events that ACT's encounter
	/// engine, mini-parse window, custom triggers, and exports all consume.
	/// </summary>
	public class EQ1Parser : UserControl, IActPluginV1
	{
		#region UI (designer)
		private System.ComponentModel.IContainer components = null;
		protected override void Dispose(bool disposing)
		{
			if (disposing && components != null) components.Dispose();
			base.Dispose(disposing);
		}

		private CheckBox cbParseSelfHeals;
		private CheckBox cbParseRunes;
		private CheckBox cbParseDoTs;
		private CheckBox cbDebugUnmatched;
		private Label lblHeader;
		private Label lblHelp;
		private TextBox tbLastUnmatched;

		private void InitializeComponent()
		{
			this.lblHeader = new Label();
			this.lblHelp = new Label();
			this.cbParseSelfHeals = new CheckBox();
			this.cbParseRunes = new CheckBox();
			this.cbParseDoTs = new CheckBox();
			this.cbDebugUnmatched = new CheckBox();
			this.tbLastUnmatched = new TextBox();
			this.SuspendLayout();

			this.lblHeader.AutoSize = true;
			this.lblHeader.Font = new Font("Microsoft Sans Serif", 9.75F, FontStyle.Bold);
			this.lblHeader.Location = new Point(6, 6);
			this.lblHeader.Text = "EverQuest 1 English Parser";

			this.lblHelp.AutoSize = true;
			this.lblHelp.Location = new Point(6, 28);
			this.lblHelp.Text =
				"Open an eqlog_<character>_<server>.txt file in ACT (File > Open Log).\r\n" +
				"This plugin will set ACT's parsing format to EQ1 automatically.\r\n" +
				"Combat actions, heals, DoT ticks, deaths, and zone changes are parsed in real time.";

			this.cbParseSelfHeals.AutoSize = true;
			this.cbParseSelfHeals.Checked = true;
			this.cbParseSelfHeals.Location = new Point(9, 90);
			this.cbParseSelfHeals.Text = "Parse heals";

			this.cbParseRunes.AutoSize = true;
			this.cbParseRunes.Checked = true;
			this.cbParseRunes.Location = new Point(9, 110);
			this.cbParseRunes.Text = "Parse rune absorption (\"You gain a rune for X points of absorption.\")";

			this.cbParseDoTs.AutoSize = true;
			this.cbParseDoTs.Checked = true;
			this.cbParseDoTs.Location = new Point(9, 130);
			this.cbParseDoTs.Text = "Parse DoT ticks (\"... has taken N damage from your <spell>\")";

			this.cbDebugUnmatched.AutoSize = true;
			this.cbDebugUnmatched.Checked = false;
			this.cbDebugUnmatched.Location = new Point(9, 160);
			this.cbDebugUnmatched.Text = "Show last unmatched combat-shaped line (debug)";

			this.tbLastUnmatched.Location = new Point(9, 184);
			this.tbLastUnmatched.Size = new Size(660, 20);
			this.tbLastUnmatched.ReadOnly = true;

			this.AutoScaleDimensions = new SizeF(6F, 13F);
			this.AutoScaleMode = AutoScaleMode.Font;
			this.Controls.Add(this.lblHeader);
			this.Controls.Add(this.lblHelp);
			this.Controls.Add(this.cbParseSelfHeals);
			this.Controls.Add(this.cbParseRunes);
			this.Controls.Add(this.cbParseDoTs);
			this.Controls.Add(this.cbDebugUnmatched);
			this.Controls.Add(this.tbLastUnmatched);
			this.Name = "EQ1Parser";
			this.Size = new Size(688, 220);
			this.ResumeLayout(false);
			this.PerformLayout();
		}
		#endregion

		public EQ1Parser() { InitializeComponent(); }

		private Label lblStatus;
		private SettingsSerializer xmlSettings;
		private readonly string settingsFile = Path.Combine(
			ActGlobals.oFormActMain.AppDataFolder.FullName, "Config\\ACT_Plugin_EQ1.config.xml");

		// ── Plugin Update ──────────────────────────────────────────────────────────
		// Set this to your plugin's registered ID on advancedcombattracker.com.
		// Once registered, replace 0 with the actual integer ID.
		private const int PluginId = 0;

		// ACT environment values we change so we can restore them on unload.
		private int prevTimeStampLen;
		private FormActMain.DateTimeLogParser prevDateParser;
		private string prevLogFileFilter;
		private string prevLogFileParentFolderName;
		private bool prevLogPathHasCharName;
		private Regex prevCharacterFileNameRegex;
		private Regex prevZoneChangeRegex;

		#region IActPluginV1
		public void InitPlugin(TabPage pluginScreenSpace, Label pluginStatusText)
		{
			lblStatus = pluginStatusText;
			pluginScreenSpace.Text = "EQ1 Parser";
			pluginScreenSpace.Controls.Add(this);
			this.Dock = DockStyle.Fill;

			xmlSettings = new SettingsSerializer(this);
			LoadSettings();

			SetupEnvironment();
			BuildRules();
			TryDetectCharacterFromCurrentLog();

			ActGlobals.oFormActMain.BeforeLogLineRead += OnBeforeLogLineRead;
			ActGlobals.oFormActMain.LogFileChanged += OnLogFileChanged;
			ActGlobals.oFormActMain.UpdateCheckClicked += OnUpdateCheckClicked;

			if (PluginId > 0)
				CheckForUpdate();

			lblStatus.Text = "EQ1 Parser started.";
		}

		public void DeInitPlugin()
		{
			try
			{
				ActGlobals.oFormActMain.BeforeLogLineRead -= OnBeforeLogLineRead;
				ActGlobals.oFormActMain.LogFileChanged -= OnLogFileChanged;
				ActGlobals.oFormActMain.UpdateCheckClicked -= OnUpdateCheckClicked;
				RestoreEnvironment();
				SaveSettings();
			}
			catch (Exception ex)
			{
				ActGlobals.oFormActMain.WriteExceptionLog(ex, "EQ1Parser DeInit");
			}
			if (lblStatus != null) lblStatus.Text = "EQ1 Parser exited.";
		}
		#endregion

		#region Plugin Update
		private void OnUpdateCheckClicked()
		{
			CheckForUpdate();
		}

		private void CheckForUpdate()
		{
			if (PluginId <= 0) return;
			try
			{
				var act = ActGlobals.oFormActMain;
				DateTime localDate = act.PluginGetSelfDateUtc(this);
				DateTime remoteDate = act.PluginGetRemoteDateUtc(PluginId);

				if (remoteDate > localDate)
				{
					var result = MessageBox.Show(
						"An update for the EQ1 Parser plugin is available.\n\n" +
						$"Local: {localDate:yyyy-MM-dd}\nRemote: {remoteDate:yyyy-MM-dd}\n\n" +
						"Download and update now?",
						"EQ1 Parser Update",
						MessageBoxButtons.YesNo,
						MessageBoxIcon.Question);

					if (result == DialogResult.Yes)
					{
						FileInfo updatedFile = act.PluginDownload(PluginId);
						ActPluginData selfData = act.PluginGetSelfData(this);
						string currentPath = selfData.pluginFile.FullName;

						// Replace the running DLL with the downloaded one.
						File.Copy(updatedFile.FullName, currentPath, true);

						MessageBox.Show(
							"Plugin updated. Please restart ACT to load the new version.",
							"EQ1 Parser Update",
							MessageBoxButtons.OK,
							MessageBoxIcon.Information);
					}
				}
			}
			catch (Exception ex)
			{
				ActGlobals.oFormActMain.WriteExceptionLog(ex, "EQ1Parser UpdateCheck");
			}
		}
		#endregion

		#region ACT environment
		// Timestamp form: "[Sat Apr 18 07:12:21 2026] " -> 27 chars including trailing space.
		private const int EQ1TimeStampLen = 27;
		private const string EQ1DateFormat = "ddd MMM dd HH:mm:ss yyyy";

		private void SetupEnvironment()
		{
			var act = ActGlobals.oFormActMain;

			prevTimeStampLen = act.TimeStampLen;
			prevDateParser = act.GetDateTimeFromLog;
			prevLogFileFilter = act.LogFileFilter;
			prevLogFileParentFolderName = act.LogFileParentFolderName;
			prevLogPathHasCharName = act.LogPathHasCharName;
			prevCharacterFileNameRegex = act.CharacterFileNameRegex;
			prevZoneChangeRegex = act.ZoneChangeRegex;

			act.TimeStampLen = EQ1TimeStampLen;
			act.GetDateTimeFromLog = ParseEqDateTime;
			act.LogFileFilter = "eqlog*.txt";
			// Don't set LogFileParentFolderName — EQ1 has no per-character subfolders.
			// LogPathHasCharName must be false — setting it true makes ACT construct
			// a character-subfolder path (EQ2-style), which produces garbage for EQ1.
			act.LogPathHasCharName = false;
			// Match the full path like the default EQ2 regex does (.+\\eq2log_...).
			// ACT uses the first capturing group as the character name.
			act.CharacterFileNameRegex = new Regex(@".+\\eqlog_([^_]+)_.+\.txt", RegexOptions.IgnoreCase);
			act.ZoneChangeRegex = new Regex(
				@"^\[.{24}\] You have entered (?<zone>.+?)\.\s*$", RegexOptions.Compiled);
		}

		private void RestoreEnvironment()
		{
			var act = ActGlobals.oFormActMain;
			act.TimeStampLen = prevTimeStampLen;
			act.GetDateTimeFromLog = prevDateParser;
			act.LogFileFilter = prevLogFileFilter;
			act.LogFileParentFolderName = prevLogFileParentFolderName;
			act.LogPathHasCharName = prevLogPathHasCharName;
			act.CharacterFileNameRegex = prevCharacterFileNameRegex;
			act.ZoneChangeRegex = prevZoneChangeRegex;
		}

		private static readonly CultureInfo EnUs = new CultureInfo("en-US");

		/// <summary>Parses the leading "[Sat Apr 18 07:12:21 2026] " timestamp.</summary>
		public static DateTime ParseEqDateTime(string logLine)
		{
			if (string.IsNullOrEmpty(logLine) || logLine.Length < EQ1TimeStampLen || logLine[0] != '[')
				return DateTime.MinValue;
			// inner = chars 1..24 (the 24-char date), then ']' at 25, ' ' at 26
			string inner = logLine.Substring(1, 24);
			if (DateTime.TryParseExact(inner, EQ1DateFormat, EnUs, DateTimeStyles.AssumeLocal, out DateTime dt))
				return dt;
			return DateTime.MinValue;
		}
		#endregion

		#region Settings I/O
		private void LoadSettings()
		{
			xmlSettings.AddControlSetting(cbParseSelfHeals.Name, cbParseSelfHeals);
			xmlSettings.AddControlSetting(cbParseRunes.Name, cbParseRunes);
			xmlSettings.AddControlSetting(cbParseDoTs.Name, cbParseDoTs);
			xmlSettings.AddControlSetting(cbDebugUnmatched.Name, cbDebugUnmatched);
			if (!File.Exists(settingsFile)) return;
			try
			{
				using (var fs = new FileStream(settingsFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
				using (var xr = new XmlTextReader(fs))
				{
					while (xr.Read())
						if (xr.NodeType == XmlNodeType.Element && xr.LocalName == "SettingsSerializer")
							xmlSettings.ImportFromXml(xr);
				}
			}
			catch (Exception ex)
			{
				if (lblStatus != null) lblStatus.Text = "Error loading settings: " + ex.Message;
			}
		}

		private void SaveSettings()
		{
			try
			{
				Directory.CreateDirectory(Path.GetDirectoryName(settingsFile));
				using (var fs = new FileStream(settingsFile, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
				using (var xw = new XmlTextWriter(fs, Encoding.UTF8) { Formatting = Formatting.Indented, Indentation = 1, IndentChar = '\t' })
				{
					xw.WriteStartDocument(true);
					xw.WriteStartElement("Config");
					xw.WriteStartElement("SettingsSerializer");
					xmlSettings.ExportToXml(xw);
					xw.WriteEndElement();
					xw.WriteEndElement();
					xw.WriteEndDocument();
					xw.Flush();
				}
			}
			catch (Exception ex)
			{
				ActGlobals.oFormActMain.WriteExceptionLog(ex, "EQ1Parser SaveSettings");
			}
		}
		#endregion

		#region Character / log file detection
		private static readonly Regex CharNameFromFile =
			new Regex(@"eqlog_(?<n>[^_]+)_.+\.txt", RegexOptions.IgnoreCase | RegexOptions.Compiled);

		private void OnLogFileChanged(bool isImport, string newLogFileName)
		{
			TryDetectCharacter(newLogFileName);
		}

		private void TryDetectCharacterFromCurrentLog()
		{
			try { TryDetectCharacter(ActGlobals.oFormActMain.LogFilePath); }
			catch { /* non-fatal */ }
		}

		private void TryDetectCharacter(string path)
		{
			try
			{
				if (string.IsNullOrEmpty(path)) return;
				// Try filename first, then full path as fallback
				var name = Path.GetFileName(path);
				var m = CharNameFromFile.Match(name);
				if (!m.Success)
					m = CharNameFromFile.Match(path);
				if (m.Success)
				{
					ActGlobals.charName = m.Groups["n"].Value;
				}
			}
			catch { /* non-fatal */ }
		}
		#endregion

		#region Parsing rules
		// The portion of the line after the timestamp is what the rules match on.
		// Each rule returns true if it consumed the line.
		private delegate bool RuleHandler(Match m, DateTime time, int gts, bool isImport, LogLineEventArgs ev);
		private sealed class Rule
		{
			public Regex Pattern;
			public RuleHandler Handler;
			public int DetectedType;
			public string Keyword; // optional fast-fail keyword
		}
		private Rule[] rules;

		// Verbs
		// Player melee verbs (1st person): subset that show up as "You <verb> X for N points of damage."
		// "frenzy" is special-cased because it appears as "You frenzy on X for N points...".
		private static readonly HashSet<string> PlayerMeleeVerbs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
		{
			"backstab","kick","slash","pierce","strike","punch","slam","crush","bash","claw","bite",
			"gore","maul","rend","shoot","slice","smash","stab","sting","sweep","hit","gnaw","chomp"
		};

		// Auto-attack verbs: only these show under "Auto-Attack" in ACT.
		// Everything else is a skill (NonMelee).
		private static readonly HashSet<string> AutoAttackVerbs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
		{
			"slash","slashes","pierce","pierces","crush","crushes"
		};

		// 3rd-person verbs that appear in "<mob> hits YOU for N..." and "<mob> tries to hit YOU, but ..."
		// These map back to a base damage-type label.
		private static readonly HashSet<string> NpcMelee3pVerbs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
		{
			"hits","kicks","cleaves","slashes","pierces","bashes","claws","bites","crushes","slams",
			"stabs","mauls","rends","smites","gores","smashes","frenzies","slices","stings","sweeps",
			"slaps","gnaws","chomps","strikes","punches","backstabs","shoots"
		};
		// Alternation pattern built from the verb set, used in regex to force correct atk/vic grouping.
		private static readonly string NpcMelee3pVerbAlt =
			"hits|kicks|cleaves|slashes|pierces|bashes|claws|bites|crushes|slams|" +
			"stabs|mauls|rends|smites|gores|smashes|frenzies|slices|stings|sweeps|" +
			"slaps|gnaws|chomps|strikes|punches|backstabs|shoots";
		// Infinitive forms used in "tries to <verb> YOU"
		private static readonly HashSet<string> NpcMeleeInfVerbs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
		{
			"hit","kick","cleave","slash","pierce","bash","claw","bite","crush","slam",
			"stab","maul","rend","smite","gore","smash","frenzy","slice","sting","sweep",
			"slap","gnaw","chomp","strike","punch","backstab","shoot"
		};

		private void BuildRules()
		{
			// Optional special suffix like " (Critical)" / " (Riposte)" / " (Flurry)" / " (Strikethrough)"
			// We capture an optional trailing parenthetical and end-of-line. EQ1 puts the period
			// BEFORE the parenthetical: "for 672 points of damage. (Critical)".
			const string Tail = @"\.?(?:\s*\((?<spec>[^)]+)\))?\s*$";

			var list = new List<Rule>();

			// 1) Player spell DD:  "You hit a foo for 12 points of magic damage by Spell Name."
			list.Add(new Rule
			{
				Keyword = " damage by ",
				DetectedType = 1,
				Pattern = new Regex(
					@"^You hit (?<vic>.+?) for (?<n>[\d,]+) points? of (?<dmgtype>\w+) damage by (?<spell>.+?)\." + @"(?:\s*\((?<spec>[^)]+)\))?$",
					RegexOptions.Compiled),
				Handler = (m, t, gts, imp, ev) => HandlePlayerSpellDamage(m, t, gts)
			});

			// 2) Third-person spell DD:  "<source> hit <victim> for N points of <type> damage by <Spell>."
			//    Catches NPC -> you ("a reanimated hand hit you for ...") and other-player -> NPC
			//    ("Qrst hit a foo for ..."). Player -> NPC is handled by rule 1 above.
			list.Add(new Rule
			{
				Keyword = " damage by ",
				DetectedType = 2,
				Pattern = new Regex(
					@"^(?<atk>.+?) hit (?<vic>.+?) for (?<n>[\d,]+) points? of (?<dmgtype>\w+) damage by (?<spell>.+?)\." + @"(?:\s*\((?<spec>[^)]+)\))?$",
					RegexOptions.Compiled),
				Handler = (m, t, gts, imp, ev) =>
				{
					if (m.Groups["atk"].Value == "You") return false; // rule 1 handles "You hit ..."
					return HandleNpcSpellDamage(m, t, gts);
				}
			});

			// 3) DoT tick from your spell: "A wan ghoul knight has taken 11 damage from your Engulfing Darkness."
			list.Add(new Rule
			{
				Keyword = "has taken",
				DetectedType = 3,
				Pattern = new Regex(
					@"^(?<vic>.+?) has taken (?<n>[\d,]+) damage from your (?<spell>.+?)\." + @"(?:\s*\((?<spec>[^)]+)\))?$",
					RegexOptions.Compiled),
				Handler = (m, t, gts, imp, ev) =>
				{
					if (!cbParseDoTs.Checked) return true;
					return HandleDoTTick(m, t, gts);
				}
			});

			// 3b) DoT tick by some other caster: "A foo has taken 11 damage by <Spell>."
			//     Source unknown -- attribute to "Unknown".
			list.Add(new Rule
			{
				Keyword = "has taken",
				DetectedType = 18,
				Pattern = new Regex(
					@"^(?<vic>.+?) has taken (?<n>[\d,]+) damage by (?<spell>.+?)\." + @"(?:\s*\((?<spec>[^)]+)\))?$",
					RegexOptions.Compiled),
				Handler = (m, t, gts, imp, ev) =>
				{
					if (!cbParseDoTs.Checked) return true;
					var victim = m.Groups["vic"].Value;
					long n = ParseLong(m.Groups["n"].Value);
					var spell = m.Groups["spell"].Value;
					if (!ActGlobals.oFormActMain.SetEncounter(t, "Unknown", victim)) return true;
					ActGlobals.oFormActMain.AddCombatAction((int)SwingTypeEnum.NonMelee, false, "DoT",
						"Unknown", spell, new Dnum(n), t, gts, victim, "dot");
					return true;
				}
			});

			// 3c) DoT damage taken by anyone, attributed to a mob/player:
			//     "You have taken N damage from <Spell> by <atk>."
			//     "Qrst has taken N damage from <Spell> by <atk>."
			list.Add(new Rule
			{
				Keyword = " taken ",
				DetectedType = 19,
				Pattern = new Regex(
					@"^(?<vic>.+?) (?:have|has) taken (?<n>[\d,]+) damage from (?<spell>.+?) by (?<atk>.+?)\." + @"(?:\s*\((?<spec>[^)]+)\))?$",
					RegexOptions.Compiled),
				Handler = (m, t, gts, imp, ev) =>
				{
					if (!cbParseDoTs.Checked) return true;
					var victim = ResolveYou(m.Groups["vic"].Value);
					var atk = m.Groups["atk"].Value;
					long n = ParseLong(m.Groups["n"].Value);
					var spell = m.Groups["spell"].Value;
					if (atk.EndsWith("'s corpse", StringComparison.Ordinal))
						atk = atk.Substring(0, atk.Length - 9);
					if (!ActGlobals.oFormActMain.SetEncounter(t, atk, victim)) return true;
					ActGlobals.oFormActMain.AddCombatAction((int)SwingTypeEnum.NonMelee, false, "DoT",
						atk, spell, new Dnum(n), t, gts, victim, "dot");
					return true;
				}
			});

			// 3d) Damage shield: "<vic> is/are <verbed> by <atk>'s <thing> for N points of non-melee damage."
			//     Examples: "...pierced by ...'s thorns ...", "...burned by ...'s flames ...".
			//     The DS source (atk) gets credit for the damage to the attacker (vic).
			list.Add(new Rule
			{
				Keyword = "non-melee damage",
				DetectedType = 20,
				Pattern = new Regex(
					@"^(?<vic>.+?) (?:is|are) (?<verb>\w+) by (?:(?<atk>.+?)'s|YOUR) (?<src>[\w ]+?) for (?<n>[\d,]+) points? of non-melee damage[.!]" + @"(?:\s*\((?<spec>[^)]+)\))?$",
					RegexOptions.Compiled),
				Handler = (m, t, gts, imp, ev) =>
				{
					var victim = ResolveYou(m.Groups["vic"].Value);
					var atk = m.Groups["atk"].Success ? m.Groups["atk"].Value
						: (string.IsNullOrEmpty(ActGlobals.charName) ? "You" : ActGlobals.charName);
					var src = m.Groups["src"].Value;
					long n = ParseLong(m.Groups["n"].Value);
					if (!ActGlobals.oFormActMain.SetEncounter(t, atk, victim)) return true;
					ActGlobals.oFormActMain.AddCombatAction((int)SwingTypeEnum.NonMelee, false, "Damage Shield",
						atk, "Damage Shield (" + src + ")", new Dnum(n), t, gts, victim, "non-melee");
					return true;
				}
			});

			// 4) Player melee (special-cased "frenzy on"):
			//    "You frenzy on a dar ghoul knight for 186 points of damage."
			list.Add(new Rule
			{
				Keyword = "points of damage",
				DetectedType = 4,
				Pattern = new Regex(
					@"^You (?<verb>frenzy) on (?<vic>.+?) for (?<n>[\d,]+) points? of damage" + Tail,
					RegexOptions.Compiled),
				Handler = (m, t, gts, imp, ev) => HandlePlayerMelee(m, t, gts, "frenzy")
			});

			// 5) Player melee normal: "You backstab/kick/slash/... a foo for N points of damage."
			list.Add(new Rule
			{
				Keyword = "points of damage",
				DetectedType = 5,
				Pattern = new Regex(
					@"^You (?<verb>\w+) (?<vic>.+?) for (?<n>[\d,]+) points? of damage" + Tail,
					RegexOptions.Compiled),
				Handler = (m, t, gts, imp, ev) =>
				{
					var verb = m.Groups["verb"].Value;
					if (!PlayerMeleeVerbs.Contains(verb)) return false;
					return HandlePlayerMelee(m, t, gts, verb);
				}
			});

			// 6) Third-person melee:  any source attacking any victim.
			//    Examples: "A foo hits YOU for N...", "Davebearpig kicks a foo for N...".
			//    Does NOT match player ("You ...") since rules 4/5 already consumed those.
			//    Uses verb alternation to force regex backtracking to find the correct verb,
			//    preventing lazy atk from stopping at "A" in "A rock golem hits YOU...".
			list.Add(new Rule
			{
				Keyword = "points of damage",
				DetectedType = 6,
				Pattern = new Regex(
					@"^(?<atk>.+?) (?<verb>" + NpcMelee3pVerbAlt + @") (?:on )?(?<vic>.+?) for (?<n>[\d,]+) points? of damage" + Tail,
					RegexOptions.Compiled),
				Handler = (m, t, gts, imp, ev) =>
				{
					var atk = m.Groups["atk"].Value;
					if (atk == "You") return false; // covered by player rules
					var verb = m.Groups["verb"].Value;
					return HandleNpcMelee(m, t, gts, verb);
				}
			});

			// 7) Player miss: "You try to <verb> a foo, but miss!"  /  "but a foo dodges!" / "but a foo's magical skin absorbs the blow!"
			//    Optional 'on ' after verb handles "You try to frenzy on <vic>, but..."
			list.Add(new Rule
			{
				Keyword = "but",
				DetectedType = 7,
				Pattern = new Regex(
					@"^You try to (?<verb>\w+) (?:on )?(?<vic>.+?), but (?<why>.+?)!?\." + @"?(?:\s*\((?<spec>[^)]+)\))?$",
					RegexOptions.Compiled),
				Handler = (m, t, gts, imp, ev) => HandlePlayerMiss(m, t, gts)
			});

			// 8) Third-party miss: "<atk> tries to <verb> <vic>, but ..."
			//    Catches NPC-vs-you, NPC-vs-other-player, and other-player-vs-NPC.
			//    Optional 'on ' after verb handles "tries to frenzy on <vic>".
			list.Add(new Rule
			{
				Keyword = "but",
				DetectedType = 8,
				Pattern = new Regex(
					@"^(?<atk>.+?) tries to (?<verb>\w+) (?:on )?(?<vic>.+?), but (?<why>.+?)!?\." + @"?(?:\s*\((?<spec>[^)]+)\))?$",
					RegexOptions.Compiled),
				Handler = (m, t, gts, imp, ev) =>
				{
					var verb = m.Groups["verb"].Value;
					if (!NpcMeleeInfVerbs.Contains(verb)) return false;
					var atk = m.Groups["atk"].Value;
					if (atk == "You") return false; // rule 7 handles "You try to ..."
					return HandleNpcMiss(m, t, gts);
				}
			});

			// 9) Heals (other -> you):  "Altheia healed you for 651 hit points by Sacred Echo."
			//    Variants: "...healed you over time for N hit points by ..."
			list.Add(new Rule
			{
				Keyword = "healed",
				DetectedType = 9,
				Pattern = new Regex(
					@"^(?<atk>.+?) healed (?<vic>you|YOU)(?<hot> over time)? for (?<n>[\d,]+) (?:\([\d,]+\) )?hit points? by (?<spell>.+?)\.$",
					RegexOptions.Compiled),
				Handler = (m, t, gts, imp, ev) =>
				{
					if (!cbParseSelfHeals.Checked) return true;
					return HandleHealOnYou(m, t, gts);
				}
			});

			// 10) Heals (X heals X self):  "Davebearpig healed himself for 2 hit points by ..."
			//     "a froglok urd shaman healed itself for 20 hit points by Inner Fire."
			list.Add(new Rule
			{
				Keyword = "healed",
				DetectedType = 10,
				Pattern = new Regex(
					@"^(?<atk>.+?) healed (?<refl>himself|herself|itself|themself|themselves)(?<hot> over time)? for (?<n>[\d,]+) (?:\([\d,]+\) )?hit points? by (?<spell>.+?)" + Tail,
					RegexOptions.Compiled),
				Handler = (m, t, gts, imp, ev) =>
				{
					if (!cbParseSelfHeals.Checked) return true;
					return HandleHealSelf(m, t, gts);
				}
			});

			// 11) Heals (X -> Y, third party):  "Altheia healed Bob for 100 hit points by Spell."
			//     Allow any non-reflexive victim (including lowercase NPCs).
			list.Add(new Rule
			{
				Keyword = "healed",
				DetectedType = 11,
				Pattern = new Regex(
					@"^(?<atk>.+?) healed (?<vic>(?!himself|herself|itself|themself|themselves|you|YOU\b).+?)(?<hot> over time)? for (?<n>[\d,]+) (?:\([\d,]+\) )?hit points? by (?<spell>.+?)" + Tail,
					RegexOptions.Compiled),
				Handler = (m, t, gts, imp, ev) =>
				{
					if (!cbParseSelfHeals.Checked) return true;
					return HandleHealOther(m, t, gts);
				}
			});

			// 12) Self-heal you (mend):  "You mend your wounds and heal some damage."
			//     EQ1 doesn't print the amount for Mend; we record a 1-pt placeholder so it appears.
			list.Add(new Rule
			{
				Keyword = "mend your wounds",
				DetectedType = 12,
				Pattern = new Regex(@"^You (?:magically )?mend your wounds and heal (?:some|considerable|all of your) damage\.$", RegexOptions.Compiled),
				Handler = (m, t, gts, imp, ev) =>
				{
					if (!cbParseSelfHeals.Checked) return true;
					var name = string.IsNullOrEmpty(ActGlobals.charName) ? "You" : ActGlobals.charName;
					if (ActGlobals.oFormActMain.SetEncounter(t, name, name))
						ActGlobals.oFormActMain.AddCombatAction((int)SwingTypeEnum.Healing, false, "None",
							name, "Mend", new Dnum(1, "Mend"), t, gts, name, "Hitpoints");
					return true;
				}
			});

			// 13) Rune absorption (self):  "You gain a rune for 35 points of absorption."
			list.Add(new Rule
			{
				Keyword = "rune for",
				DetectedType = 13,
				Pattern = new Regex(@"^You gain a rune for (?<n>[\d,]+) points? of absorption\.$", RegexOptions.Compiled),
				Handler = (m, t, gts, imp, ev) =>
				{
					if (!cbParseRunes.Checked) return true;
					var name = string.IsNullOrEmpty(ActGlobals.charName) ? "You" : ActGlobals.charName;
					long n = ParseLong(m.Groups["n"].Value);
					if (ActGlobals.oFormActMain.SetEncounter(t, name, name))
						ActGlobals.oFormActMain.AddCombatAction((int)SwingTypeEnum.Healing, false, "None",
							name, "Rune", new Dnum(n), t, gts, name, "Absorption");
					return true;
				}
			});

			// 14) Death messages
			list.Add(new Rule
			{
				Keyword = "slain",
				DetectedType = 14,
				Pattern = new Regex(@"^You have slain (?<vic>.+?)!\.?$", RegexOptions.Compiled),
				Handler = (m, t, gts, imp, ev) =>
				{
					var atk = string.IsNullOrEmpty(ActGlobals.charName) ? "You" : ActGlobals.charName;
					var vic = m.Groups["vic"].Value;
					if (ActGlobals.oFormActMain.InCombat || ActGlobals.oFormActMain.SetEncounter(t, atk, vic))
						ActGlobals.oFormActMain.AddCombatAction((int)SwingTypeEnum.NonMelee, false, "None",
							atk, "Killing", Dnum.Death, t, gts, vic, "Death");
					return true;
				}
			});
			list.Add(new Rule
			{
				Keyword = "slain by",
				DetectedType = 15,
				Pattern = new Regex(@"^You have been slain by (?<atk>.+?)!\.?$", RegexOptions.Compiled),
				Handler = (m, t, gts, imp, ev) =>
				{
					var vic = string.IsNullOrEmpty(ActGlobals.charName) ? "You" : ActGlobals.charName;
					var atk = m.Groups["atk"].Value;
					if (ActGlobals.oFormActMain.InCombat || ActGlobals.oFormActMain.SetEncounter(t, atk, vic))
						ActGlobals.oFormActMain.AddCombatAction((int)SwingTypeEnum.NonMelee, false, "None",
							atk, "Killing", Dnum.Death, t, gts, vic, "Death");
					return true;
				}
			});
			list.Add(new Rule
			{
				Keyword = "has been slain by",
				DetectedType = 16,
				Pattern = new Regex(@"^(?<vic>.+?) has been slain by (?<atk>.+?)!\.?$", RegexOptions.Compiled),
				Handler = (m, t, gts, imp, ev) =>
				{
					var atk = m.Groups["atk"].Value;
					var vic = m.Groups["vic"].Value;
					if (ActGlobals.oFormActMain.InCombat || ActGlobals.oFormActMain.SetEncounter(t, atk, vic))
						ActGlobals.oFormActMain.AddCombatAction((int)SwingTypeEnum.NonMelee, false, "None",
							atk, "Killing", Dnum.Death, t, gts, vic, "Death");
					return true;
				}
			});

			// 17) Zone change
			list.Add(new Rule
			{
				Keyword = "have entered",
				DetectedType = 17,
				Pattern = new Regex(@"^You have entered (?<zone>.+?)\.?$", RegexOptions.Compiled),
				Handler = (m, t, gts, imp, ev) =>
				{
					ActGlobals.oFormActMain.ChangeZone(m.Groups["zone"].Value.Trim());
					return true;
				}
			});

			// 21) Damage shield absorbed (no damage dealt, but suppresses unmatched-line noise):
			//     "<vic>'s magical skin absorbs the damage of <atk>'s flames."
			//     "YOUR magical skin absorbs the damage of <atk>'s thorns."
			list.Add(new Rule
			{
				Keyword = "absorbs the damage",
				DetectedType = 21,
				Pattern = new Regex(@"^(?:(?<vic>.+?)'s|YOUR) magical skin absorbs the damage of (?:(?<atk>.+?)'s|YOUR) (?<src>[\w ]+?)\.$", RegexOptions.Compiled),
				Handler = (m, t, gts, imp, ev) => true
			});

			// 22) Generic non-melee damage taken (source unknown):
			//     "You were hit by non-melee for 389 damage."
			list.Add(new Rule
			{
				Keyword = "hit by non-melee",
				DetectedType = 22,
				Pattern = new Regex(@"^You were hit by non-melee for (?<n>[\d,]+) damage\.$", RegexOptions.Compiled),
				Handler = (m, t, gts, imp, ev) =>
				{
					var vic = string.IsNullOrEmpty(ActGlobals.charName) ? "You" : ActGlobals.charName;
					long n = ParseLong(m.Groups["n"].Value);
					if (!ActGlobals.oFormActMain.SetEncounter(t, "Unknown", vic)) return true;
					ActGlobals.oFormActMain.AddCombatAction((int)SwingTypeEnum.NonMelee, false, "None",
						"Unknown", "Non-melee", new Dnum(n), t, gts, vic, "non-melee");
					return true;
				}
			});

			// 23) Spell resist: "<NPC> resisted your <Spell>!"
			//     "<NPC> resisted <Player>'s <Spell>!"
			list.Add(new Rule
			{
				Keyword = "resisted",
				DetectedType = 23,
				Pattern = new Regex(
					@"^(?<vic>.+?) resisted (?:your|(?<atk>.+?)'s) (?<spell>.+?)!$",
					RegexOptions.Compiled),
				Handler = (m, t, gts, imp, ev) =>
				{
					var victim = m.Groups["vic"].Value;
					var atk = m.Groups["atk"].Success ? m.Groups["atk"].Value
						: (string.IsNullOrEmpty(ActGlobals.charName) ? "You" : ActGlobals.charName);
					var spell = m.Groups["spell"].Value;
					if (!ActGlobals.oFormActMain.SetEncounter(t, atk, victim)) return true;
					ActGlobals.oFormActMain.AddCombatAction((int)SwingTypeEnum.NonMelee, false, "Resist",
						atk, spell, new Dnum(-9, "resist"), t, gts, victim, "magic");
					return true;
				}
			});

			// 24) "You died." (no attacker specified — different from "slain by X")
			list.Add(new Rule
			{
				Keyword = "died",
				DetectedType = 24,
				Pattern = new Regex(@"^You died\.$", RegexOptions.Compiled),
				Handler = (m, t, gts, imp, ev) =>
				{
					var vic = string.IsNullOrEmpty(ActGlobals.charName) ? "You" : ActGlobals.charName;
					if (ActGlobals.oFormActMain.InCombat)
						ActGlobals.oFormActMain.AddCombatAction((int)SwingTypeEnum.NonMelee, false, "None",
							"Unknown", "Killing", Dnum.Death, t, gts, vic, "Death");
					return true;
				}
			});

			// 25) Taunt: "<Player> has captured <NPC>'s attention!"
			//     "<Player> was partially successful in capturing <NPC>'s attention."
			//     "<Player> has captured <NPC>'s attention with an unparalleled approach!"
			list.Add(new Rule
			{
				Keyword = "attention",
				DetectedType = 25,
				Pattern = new Regex(@"^(?<atk>.+?) (?:has captured|capture|was partially successful in capturing) (?<vic>.+?)'s attention\b.*[.!]$", RegexOptions.Compiled),
				Handler = (m, t, gts, imp, ev) =>
				{
					// Taunts are informational — mark the encounter but don't add damage.
					var atk = ResolveYou(m.Groups["atk"].Value);
					var victim = m.Groups["vic"].Value;
					if (!ActGlobals.oFormActMain.SetEncounter(t, atk, victim)) return true;
					// Record as a 0-damage non-melee so it shows in the swing log as "Taunt"
					ActGlobals.oFormActMain.AddCombatAction((int)SwingTypeEnum.NonMelee, false, "None",
						atk, "Taunt", Dnum.NoDamage, t, gts, victim, "taunt");
					return true;
				}
			});

			rules = list.ToArray();
		}
		#endregion

		#region Damage / heal handlers
		private static readonly HashSet<string> SpecialFlags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
		{
			"Critical","Crippling Blow","Crippling","Lucky","Riposte","Flurry","Wild Rampage","Strikethrough","Twincast","Locked","Finishing Blow","Deadly Strike","Assassinate","Headshot"
		};

		private static void ReadSpecial(Match m, out bool crit, out string special)
		{
			string spec = m.Groups["spec"].Success ? m.Groups["spec"].Value : null;
			crit = false;
			special = "None";
			if (!string.IsNullOrEmpty(spec))
			{
				crit = spec.IndexOf("Critical", StringComparison.OrdinalIgnoreCase) >= 0
					|| spec.IndexOf("Crippling", StringComparison.OrdinalIgnoreCase) >= 0
					|| spec.IndexOf("Deadly Strike", StringComparison.OrdinalIgnoreCase) >= 0
					|| spec.IndexOf("Assassinate", StringComparison.OrdinalIgnoreCase) >= 0
					|| spec.IndexOf("Headshot", StringComparison.OrdinalIgnoreCase) >= 0
					|| spec.IndexOf("Finishing Blow", StringComparison.OrdinalIgnoreCase) >= 0;
				special = spec;
			}
		}

		private static long ParseLong(string s)
		{
			long n;
			if (string.IsNullOrEmpty(s)) return 0;
			if (long.TryParse(s.Replace(",", ""), NumberStyles.Integer, EnUs, out n)) return n;
			return 0;
		}

		private static string DamageTypeForVerb(string verb)
		{
			// EQ1 melee has no explicit elemental flavor; map verb -> a generic damage-type label
			// that ACT shows in the DamageType column.
			switch (verb.ToLowerInvariant())
			{
				case "slash": case "slashes": case "slice": case "slices": case "rend": case "rends": return "slashing";
				case "pierce": case "pierces": case "stab": case "stabs": case "sting": case "stings":
				case "backstab": case "backstabs": case "shoot": case "shoots": case "gore": case "gores": return "piercing";
				case "crush": case "crushes": case "smash": case "smashes": case "slam": case "slams":
				case "bash": case "bashes": case "punch": case "punches": case "strike": case "strikes":
				case "kick": case "kicks": case "frenzy": case "frenzies": case "maul": case "mauls":
				case "claw": case "claws": case "bite": case "bites": case "gnaw": case "gnaws":
				case "chomp": case "chomps": case "slap": case "slaps": case "sweep": case "sweeps":
				case "hit": case "hits": case "smite": case "smites": case "cleave": case "cleaves":
					return "crushing";
				default: return "melee";
			}
		}

		private string ResolveYou(string s)
		{
			if (string.IsNullOrEmpty(s)) return s;
			if (s == "YOU" || s == "you" || s == "You" || s == "YOUR" || s == "Your")
				return string.IsNullOrEmpty(ActGlobals.charName) ? "You" : ActGlobals.charName;
			return s;
		}

		/// <summary>Converts 3rd-person verb to base form so "hits" and "hit" both display as "Hit" in ACT.</summary>
		private static string NormalizeVerb(string verb)
		{
			if (string.IsNullOrEmpty(verb)) return verb;
			// frenzies → frenzy
			if (verb.EndsWith("ies", StringComparison.OrdinalIgnoreCase))
				return verb.Substring(0, verb.Length - 3) + "y";
			// cleaves → cleave
			if (verb.EndsWith("ves", StringComparison.OrdinalIgnoreCase))
				return verb.Substring(0, verb.Length - 1);
			// slashes, crushes, bashes → slash, crush, bash
			if (verb.EndsWith("shes", StringComparison.OrdinalIgnoreCase) ||
				verb.EndsWith("ches", StringComparison.OrdinalIgnoreCase))
				return verb.Substring(0, verb.Length - 2);
			// pierces → pierce
			if (verb.EndsWith("es", StringComparison.OrdinalIgnoreCase))
				return verb.Substring(0, verb.Length - 1);
			// hits, kicks, slams → hit, kick, slam
			if (verb.EndsWith("s", StringComparison.OrdinalIgnoreCase) && verb.Length > 2)
				return verb.Substring(0, verb.Length - 1);
			return verb;
		}

		private static string CapFirst(string s)
		{
			if (string.IsNullOrEmpty(s)) return s;
			return char.ToUpperInvariant(s[0]) + s.Substring(1);
		}

		private bool HandlePlayerMelee(Match m, DateTime t, int gts, string verb)
		{
			var attacker = string.IsNullOrEmpty(ActGlobals.charName) ? "You" : ActGlobals.charName;
			var victim = m.Groups["vic"].Value;
			long n = ParseLong(m.Groups["n"].Value);
			bool crit; string special; ReadSpecial(m, out crit, out special);
			if (attacker == victim) return true; // sanity
			if (!ActGlobals.oFormActMain.SetEncounter(t, attacker, victim)) return true;
			var swingType = AutoAttackVerbs.Contains(verb) ? (int)SwingTypeEnum.Melee : (int)SwingTypeEnum.NonMelee;
			ActGlobals.oFormActMain.AddCombatAction(swingType, crit, special,
				attacker, CapFirst(verb), new Dnum(n), t, gts, victim, DamageTypeForVerb(verb));
			return true;
		}

		private bool HandleNpcMelee(Match m, DateTime t, int gts, string verb)
		{
			var attacker = m.Groups["atk"].Value;
			var victim = ResolveYou(m.Groups["vic"].Value);
			long n = ParseLong(m.Groups["n"].Value);
			bool crit; string special; ReadSpecial(m, out crit, out special);
			if (attacker == victim) return true;
			if (!ActGlobals.oFormActMain.SetEncounter(t, attacker, victim)) return true;
			var baseVerb = NormalizeVerb(verb);
			var swingType = AutoAttackVerbs.Contains(verb) ? (int)SwingTypeEnum.Melee : (int)SwingTypeEnum.NonMelee;
			ActGlobals.oFormActMain.AddCombatAction(swingType, crit, special,
				attacker, CapFirst(baseVerb), new Dnum(n), t, gts, victim, DamageTypeForVerb(verb));
			return true;
		}

		private bool HandlePlayerSpellDamage(Match m, DateTime t, int gts)
		{
			var attacker = string.IsNullOrEmpty(ActGlobals.charName) ? "You" : ActGlobals.charName;
			var victim = m.Groups["vic"].Value;
			long n = ParseLong(m.Groups["n"].Value);
			bool crit; string special; ReadSpecial(m, out crit, out special);
			var dmgType = m.Groups["dmgtype"].Value.ToLowerInvariant();
			var spell = m.Groups["spell"].Value;
			if (attacker == victim) return true;
			if (!ActGlobals.oFormActMain.SetEncounter(t, attacker, victim)) return true;
			ActGlobals.oFormActMain.AddCombatAction((int)SwingTypeEnum.NonMelee, crit, special,
				attacker, spell, new Dnum(n), t, gts, victim, dmgType);
			return true;
		}

		private bool HandleNpcSpellDamage(Match m, DateTime t, int gts)
		{
			var attacker = m.Groups["atk"].Value;
			var victim = ResolveYou(m.Groups["vic"].Value);
			long n = ParseLong(m.Groups["n"].Value);
			bool crit; string special; ReadSpecial(m, out crit, out special);
			var dmgType = m.Groups["dmgtype"].Value.ToLowerInvariant();
			var spell = m.Groups["spell"].Value;
			if (attacker == victim) return true;
			if (!ActGlobals.oFormActMain.SetEncounter(t, attacker, victim)) return true;
			ActGlobals.oFormActMain.AddCombatAction((int)SwingTypeEnum.NonMelee, crit, special,
				attacker, spell, new Dnum(n), t, gts, victim, dmgType);
			return true;
		}

		private bool HandleDoTTick(Match m, DateTime t, int gts)
		{
			var attacker = string.IsNullOrEmpty(ActGlobals.charName) ? "You" : ActGlobals.charName;
			var victim = m.Groups["vic"].Value;
			long n = ParseLong(m.Groups["n"].Value);
			var spell = m.Groups["spell"].Value;
			if (!ActGlobals.oFormActMain.SetEncounter(t, attacker, victim)) return true;
			ActGlobals.oFormActMain.AddCombatAction((int)SwingTypeEnum.NonMelee, false, "DoT",
				attacker, spell, new Dnum(n), t, gts, victim, "dot");
			return true;
		}

		private bool HandlePlayerMiss(Match m, DateTime t, int gts)
		{
			var attacker = string.IsNullOrEmpty(ActGlobals.charName) ? "You" : ActGlobals.charName;
			var verb = m.Groups["verb"].Value;
			var victim = m.Groups["vic"].Value;
			var why = m.Groups["why"].Value;
			bool _crit; string special; ReadSpecial(m, out _crit, out special);

			Dnum failType;
			string damageType;
			GetFailType(why, victim, out failType, out damageType);

			if (attacker == victim) return true;
			if (!ActGlobals.oFormActMain.SetEncounter(t, attacker, victim)) return true;
			var swingType = AutoAttackVerbs.Contains(verb) ? (int)SwingTypeEnum.Melee : (int)SwingTypeEnum.NonMelee;
			ActGlobals.oFormActMain.AddCombatAction(swingType, false, special,
				attacker, CapFirst(verb), failType, t, gts, victim, damageType);
			return true;
		}

		private bool HandleNpcMiss(Match m, DateTime t, int gts)
		{
			var attacker = m.Groups["atk"].Value;
			var verb = m.Groups["verb"].Value;
			var victim = ResolveYou(m.Groups["vic"].Value);
			var why = m.Groups["why"].Value;
			bool _crit; string special; ReadSpecial(m, out _crit, out special);

			Dnum failType;
			string damageType;
			GetFailType(why, victim, out failType, out damageType);

			if (attacker == victim) return true;
			if (!ActGlobals.oFormActMain.SetEncounter(t, attacker, victim)) return true;
			var swingType = AutoAttackVerbs.Contains(verb) ? (int)SwingTypeEnum.Melee : (int)SwingTypeEnum.NonMelee;
			ActGlobals.oFormActMain.AddCombatAction(swingType, false, special,
				attacker, CapFirst(NormalizeVerb(verb)), failType, t, gts, victim, damageType);
			return true;
		}

		private static void GetFailType(string why, string victim, out Dnum fail, out string dmgType)
		{
			// Normalize "why":
			//   "miss" / "misses"
			//   "<victim> dodges" / "<victim>'s magical skin absorbs the blow"
			//   "YOU dodge" / "YOU block" / "YOU riposte" / "YOU parry"
			//   "<victim> ripostes" etc
			string w = (why ?? "").Trim().ToLowerInvariant();
			dmgType = "melee";
			if (w == "miss" || w == "misses") { fail = Dnum.Miss; return; }

			if (w.Contains("magical skin absorbs")) { fail = new Dnum(-9, "absorbed"); return; }
			if (w.Contains("invulnerable")) { fail = new Dnum(-9, "invulnerable"); return; }

			if (w.Contains("dodge")) { fail = new Dnum(-9, "dodge"); return; }
			if (w.Contains("parry") || w.Contains("parries")) { fail = new Dnum(-9, "parry"); return; }
			if (w.Contains("block")) { fail = new Dnum(-9, "block"); return; }
			if (w.Contains("riposte")) { fail = new Dnum(-9, "riposte"); return; }
			if (w.Contains("resist")) { fail = new Dnum(-9, "resist"); return; }

			fail = new Dnum(-9, why);
		}

		private bool HandleHealOnYou(Match m, DateTime t, int gts)
		{
			var attacker = m.Groups["atk"].Value;
			var victim = string.IsNullOrEmpty(ActGlobals.charName) ? "You" : ActGlobals.charName;
			long n = ParseLong(m.Groups["n"].Value);
			var spell = m.Groups["spell"].Value;
			if (!ActGlobals.oFormActMain.SetEncounter(t, attacker, victim)) return true;
			ActGlobals.oFormActMain.AddCombatAction((int)SwingTypeEnum.Healing, false,
				m.Groups["hot"].Success ? "HoT" : "None",
				attacker, spell, new Dnum(n), t, gts, victim, "Hitpoints");
			return true;
		}

		private bool HandleHealSelf(Match m, DateTime t, int gts)
		{
			var attacker = m.Groups["atk"].Value;
			long n = ParseLong(m.Groups["n"].Value);
			var spell = m.Groups["spell"].Value;
			// Self-heal: encounter is whatever is currently active; otherwise skip.
			if (!ActGlobals.oFormActMain.InCombat) return true;
			ActGlobals.oFormActMain.AddCombatAction((int)SwingTypeEnum.Healing, false, "None",
				attacker, spell, new Dnum(n), t, gts, attacker, "Hitpoints");
			return true;
		}

		private bool HandleHealOther(Match m, DateTime t, int gts)
		{
			var attacker = ResolveYou(m.Groups["atk"].Value);
			var victim = ResolveYou(m.Groups["vic"].Value);
			long n = ParseLong(m.Groups["n"].Value);
			var spell = m.Groups["spell"].Value;
			if (!ActGlobals.oFormActMain.SetEncounter(t, attacker, victim)) return true;
			ActGlobals.oFormActMain.AddCombatAction((int)SwingTypeEnum.Healing, false,
				m.Groups["hot"].Success ? "HoT" : "None",
				attacker, spell, new Dnum(n), t, gts, victim, "Hitpoints");
			return true;
		}
		#endregion

		#region Log line dispatcher
		// Quick reject keywords -- one of these must appear before we try the regex array.
		private static readonly string[] FastKeywords = new[]
		{
			"points of damage", "damage by", "has taken", "but ",
			"healed", "mend your wounds", "rune for", "have entered",
			"slain", "magical skin absorbs", "resisted", "died", "attention",
			"hit by non-melee"
		};

		private void OnBeforeLogLineRead(bool isImport, LogLineEventArgs ev)
		{
			try
			{
				// Lazy character-name detection: if charName is still unset, try once
				if (string.IsNullOrEmpty(ActGlobals.charName))
					TryDetectCharacterFromCurrentLog();

				string raw = ev.logLine;
				if (string.IsNullOrEmpty(raw) || raw.Length <= EQ1TimeStampLen) return;
				if (raw[0] != '[') return;

				string body = raw.Substring(EQ1TimeStampLen);
				if (!ContainsAnyFastKeyword(body)) return;

				DateTime t = ev.detectedTime;
				if (t == DateTime.MinValue)
				{
					t = ParseEqDateTime(raw);
					if (t == DateTime.MinValue) t = ActGlobals.oFormActMain.LastKnownTime;
				}
				int gts = ActGlobals.oFormActMain.GlobalTimeSorter;

				bool matched = false;
				for (int i = 0; i < rules.Length; i++)
				{
					var r = rules[i];
					if (!string.IsNullOrEmpty(r.Keyword) && body.IndexOf(r.Keyword, StringComparison.Ordinal) < 0)
						continue;
					Match m = r.Pattern.Match(body);
					if (!m.Success) continue;
					if (r.Handler(m, t, gts, isImport, ev))
					{
						ev.detectedType = r.DetectedType;
						matched = true;
						break;
					}
				}

				if (!matched && cbDebugUnmatched.Checked && LooksCombatLike(body))
				{
					try
					{
						if (this.IsHandleCreated)
							this.BeginInvoke((Action)(() => { tbLastUnmatched.Text = body; }));
					}
					catch { /* not fatal */ }
				}
			}
			catch (Exception ex)
			{
				ActGlobals.oFormActMain.WriteExceptionLog(ex, "EQ1Parser line: " + (ev != null ? ev.logLine : ""));
			}
		}

		private static bool ContainsAnyFastKeyword(string body)
		{
			for (int i = 0; i < FastKeywords.Length; i++)
				if (body.IndexOf(FastKeywords[i], StringComparison.Ordinal) >= 0)
					return true;
			return false;
		}

		private static bool LooksCombatLike(string body)
		{
			return body.IndexOf(" damage", StringComparison.Ordinal) >= 0
				|| body.IndexOf(" healed", StringComparison.Ordinal) >= 0
				|| body.IndexOf(" slain", StringComparison.Ordinal) >= 0;
		}
		#endregion
	}
}
