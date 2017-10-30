using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using Discord;
using Discord.WebSocket;
using Discord.Net.Providers.WS4Net;
using System.Net;
using System.Text.RegularExpressions;
using System.Text;
using System.Collections.Immutable;

namespace dIRCd
{
	internal class BridgeServer
	{
		public BridgeConfig bridgeConfig;
		private LogCallback output;
		private DiscordSocketClient discordClient;
		internal Dictionary<string, IrcServer> ircServers = new Dictionary<string, IrcServer>();
		private static Regex mentionRegex = new Regex(@"<(@!?|#|@&)(\d{1,20})>", RegexOptions.Compiled);
		private static Regex smileyRegex = new Regex(@"<(:.*?:)(\d{1,20})>", RegexOptions.Compiled);
		private const string SERVER_DMS = "DMServer";

		private DiscordSocketConfig discordSocketConfig = new DiscordSocketConfig
		{
			AlwaysDownloadUsers = true,
			DefaultRetryMode = RetryMode.Retry502 | RetryMode.RetryTimeouts,
			LargeThreshold = 0,
			LogLevel = LogSeverity.Debug,
			MessageCacheSize = 0,
			WebSocketProvider = WS4NetProvider.Instance
		};

		public BridgeServer(LogCallback logCallback, BridgeConfig config)
		{
			output = logCallback;
			bridgeConfig = config;
			discordClient = new DiscordSocketClient(discordSocketConfig);
			
			#region Events

			discordClient.Log += Log;
			discordClient.MessageReceived += ProcessDiscordMessage;
			discordClient.Ready += DiscordReady;
			discordClient.UserJoined += DiscordUserJoined;
			discordClient.UserLeft += DiscordUserLeft;
			discordClient.UserUpdated += DiscordUserUpdated;
			discordClient.GuildMemberUpdated += DiscordUserUpdated;
			discordClient.CurrentUserUpdated += DiscordSelfUpdated;
			discordClient.ChannelCreated += DiscordChannelCreated;

			#endregion
		}

		public async void Run()
		{
			try
			{
				await Log(LogSeverity.Info, "Discord client starting");
				await discordClient.LoginAsync(TokenType.User, bridgeConfig.token);
				await Log(LogSeverity.Info, "Discord client logged in, connecting to guilds...");
				await discordClient.StartAsync();
			}
			catch (Exception e)
			{
				await Log(LogSeverity.Critical, "Unrecoverable exception: ", e);
			}
		}

		internal async void Shutdown()
		{
			foreach (IrcServer server in ircServers.Values)
			{
				await server.Shutdown();
			}

			if (discordClient != null)
			{
				await Log(LogSeverity.Info, "Discord client shutting down...");
				await discordClient.LogoutAsync();
				await discordClient.StopAsync();
				discordClient.Dispose();
				discordClient = null;
				await Log(LogSeverity.Info, "Discord client disconnected and shut down.");
			}
		}

		private async void StartIrcServer(string guildId, string guildName, IReadOnlyCollection<IChannel> channels, IPAddress address, int port)
		{
			CancellationTokenSource canceller = new CancellationTokenSource();
			IrcServerConfig serverConfig = new IrcServerConfig
			{
				serverName = guildName.Replace(" ", String.Empty) + ".Discord",
				guildId = guildId,
				guildName = guildName,
				startTime = DateTime.Now
			};

			IrcServer server = new IrcServer(this, address, port, serverConfig);
			ircServers[guildId] = server;
			await server.Run(channels.Where(e => !bridgeConfig.excludedChannels.Contains(e.Id)));
		}

		private Task ProcessDiscordMessage(SocketMessage message)
		{
			Log(LogSeverity.Debug, $"Message recieved from channel {message.Channel.Name} (Id {message.Channel.Id})");

			if (message.Channel is SocketTextChannel guildChannel)
			{
				if (ircServers.TryGetValue(guildChannel.Guild.Id.ToString(), out IrcServer server))
				{
					if (message.Author.Id != discordClient.CurrentUser.Id || !server.IsOwnMessage(message.Content))
					{
						server.SendMessage("#" + guildChannel.Name.Sanitize(), ParseDiscordToPlaintext(message), message.Author.IrcHostname());
					}
				}
				else
				{
					Log(LogSeverity.Verbose, $"No connections on server for guild {guildChannel.Guild.Name}");
				}
			}
			else if (message.Channel is SocketDMChannel dmChannel)
			{
				if (ircServers.TryGetValue(SERVER_DMS, out IrcServer server))
				{
					server.JoinDMChannel(dmChannel);
					if (message.Author.Id != discordClient.CurrentUser.Id)
					{
						server.SendMessage(discordClient.CurrentUser.IrcHostname(), ParseDiscordToPlaintext(message), dmChannel.Recipient.IrcHostname());
					}
					else
					{
						if (!server.IsOwnMessage(message.Content)) {
							server.SendMessage(discordClient.CurrentUser.IrcHostname(), "::dIRCd:: [You]: " + ParseDiscordToPlaintext(message), dmChannel.Recipient.IrcHostname());
						}
					}
				}
			}
			else if (message.Channel is SocketGroupChannel groupChannel)
			{
				if (ircServers.TryGetValue(SERVER_DMS, out IrcServer server))
				{
					if (message.Author.Id != discordClient.CurrentUser.Id || !server.IsOwnMessage(message.Content))
					{
						server.SendMessage("&" + groupChannel.Name, ParseDiscordToPlaintext(message), message.Author.IrcHostname());
					}
				}
			}
			else
			{
				Log(LogSeverity.Verbose, $"{message.Channel.Name} is not a guild text channel; only guild text channels are supported.");
			}

			return Task.CompletedTask;
		}

		private string ParseDiscordToPlaintext(SocketMessage message) {
			string messageBody = message.Content;
			messageBody = ParseMentions(messageBody, message);
			messageBody = ParseSmileys(messageBody, message);
			messageBody = AddAttachments(messageBody, message);
			messageBody = AddEmbeds(messageBody, message);
			return messageBody;
		}

		private string ParseMentions(string messageBody, SocketMessage message)
		{
			return mentionRegex.Replace(messageBody, (e => LookupMention(e, message)));
		}

		private string LookupMention(Match m, SocketMessage message)
		{
			string type = m.Groups[1].Value;
			ulong id = ulong.Parse(m.Groups[2].Value);
			switch (type)
			{
				case "@":
				case "@!":
					return $"@{message.MentionedUsers.First(e => e.Id == id).IrcNick()}";
				case "#":
					return $"#{message.MentionedChannels.First(e => e.Id == id).Name.Sanitize()}";
				case "@&":
					return $"@[{message.MentionedRoles.First(e => e.Id == id).Name}]";
				default:
					throw new ArgumentException($"Illegal argument for mention lookup: {type}");
			}
		}

		private string ParseSmileys(string messageBody, SocketMessage message)
		{
			return smileyRegex.Replace(messageBody, (e => LookupSmiley(e, message)));
		}

		private string LookupSmiley(Match m, SocketMessage message)
		{
			string name = m.Groups[1].Value;
			return bridgeConfig.smileyMapping.TryGetValue(name, out string result) ? result : name;
		}

		private string AddAttachments(string messageBody, SocketMessage message)
		{
			StringBuilder attachmentMessage = new StringBuilder(messageBody);
			foreach (Attachment attachment in message.Attachments)
			{
				attachmentMessage.Append(" ").Append(attachment.Url);
			}

			return attachmentMessage.ToString();
		}

		private string AddEmbeds(string messageBody, SocketMessage message)
		{
			StringBuilder embeddedMessage = new StringBuilder(messageBody);
			foreach (Embed embed in message.Embeds)
			{
				List<string> embedData = ParseEmbed(embed);

				/*foreach (string embedElement in embedData)
				{
					if (!messageBody.Contains(embedElement))
					{
						embeddedMessage.Append(" ").Append(embedElement);
					}
				}*/
			}

			return embeddedMessage.ToString();
		}

		private List<string> ParseEmbed(Embed embed)
		{
			Log(LogSeverity.Debug, $"Parsing embed of type {embed.Type}, Provider: {embed.Provider?.Name} / {embed.Provider?.Url}");

			List<string> embedData = new List<string>();
			foreach (string data in new string[] { embed.Url, embed.Image?.Url, embed.Video?.Url })
			{
				if (!string.IsNullOrWhiteSpace(data) && !embedData.Contains(data))
				{
					Log(LogSeverity.Debug, $"\tAdding URL {data}");
					embedData.Add(data);
				}
			}

			return embedData;
		}

		private Task DiscordReady()
		{
			StartIrcServer(SERVER_DMS, "DMs", discordClient.PrivateChannels, IPAddress.Loopback, 6699);

			int port = 6700;
			foreach (SocketGuild guild in discordClient.Guilds.Where(e => !bridgeConfig.excludedGuilds.Contains(e.Id)).OrderBy(e => e.Users.Single(u => u.Id == discordClient.CurrentUser.Id).JoinedAt))
			{
				StartIrcServer(guild.Id.ToString(), guild.Name, guild.TextChannels, IPAddress.Loopback, port);
				port++;
			}

			return Task.CompletedTask;
		}

		private Task DiscordUserJoined(SocketGuildUser user)
		{
			if (ircServers.TryGetValue(user.Guild.Id.ToString(), out IrcServer server))
			{
				server.SendUserJoin(user);
			}
			return Log(LogSeverity.Info, $"User join message for {user.Username}");
		}

		private Task DiscordUserLeft(SocketGuildUser user)
		{
			if (ircServers.TryGetValue(user.Guild.Id.ToString(), out IrcServer server))
			{
				server.SendUserQuit(user);
			}
			return Log(LogSeverity.Info, $"User left message for {user.Username}");
		}

		private Task DiscordUserUpdated(SocketUser oldUser, SocketUser newUser)
		{
			if(oldUser.IrcNick() != newUser.IrcNick())
			{
				if (ircServers.TryGetValue(SERVER_DMS, out IrcServer dmServer))
				{
					dmServer.SendUserNickchange(oldUser.IrcHostname(), newUser);
				}

				foreach (SocketGuild guild in discordClient.Guilds)
				{
					if (guild.Users.Contains(newUser) && ircServers.TryGetValue(guild.Id.ToString(), out IrcServer guildServer))
					{
						guildServer.SendUserNickchange(oldUser.IrcHostname(), newUser);
					}
				}
				return Log(LogSeverity.Info, $"{oldUser.IrcNick()} changed name to {newUser.IrcNick()}");
			}
			return Task.CompletedTask;
		}

		private Task DiscordSelfUpdated(SocketSelfUser oldUser, SocketSelfUser newUser)
		{
			foreach (IrcServer server in ircServers.Values)
			{
				server.SendSelfNickchange(newUser.IrcNick());
			}
			return Log(LogSeverity.Info, $"Own name changed from {oldUser.IrcNick()} to {newUser.IrcNick()}");
		}

		private Task DiscordChannelCreated(SocketChannel newChannel)
		{
			if (newChannel is SocketTextChannel guildChannel)
			{
				if (ircServers.TryGetValue(guildChannel.Guild.Id.ToString(), out IrcServer server))
				{
					server.JoinGuildChannel(guildChannel);
					return Log(LogSeverity.Info, $"Joined new guild channel #{guildChannel.Name.Sanitize()}");
				}
				else
				{
					// We'll automatically join this next time we connect to the guild server
					return Task.CompletedTask;
				}
			}
			else if (newChannel is SocketGroupChannel groupChannel)
			{
				if (ircServers.TryGetValue(SERVER_DMS, out IrcServer server))
				{
					server.JoinGroupChannel(groupChannel);
					return Log(LogSeverity.Info, $"Joined new group dm channel &{groupChannel.Name.Sanitize()}");
				}
				else
				{
					// We'll automatically join this next time we connect to the dm server
					return Task.CompletedTask;
				}
			}
			else if (newChannel is SocketDMChannel dmChannel)
			{
				// We handle DM channels when receiving messages, since IRC doesn't handle them as persisent entities
				return Task.CompletedTask;
			}
			else
			{
				// ignore all other channel updates
				return Task.CompletedTask;
			}
		}

		internal SocketUser GetOwnUser(string guildId)
		{
			if (SERVER_DMS == guildId)
			{
				return discordClient.CurrentUser;
			}
			else
			{
				return discordClient.GetGuild(ulong.Parse(guildId)).CurrentUser;
			}
		}

		internal void UnknownChannelMessage(string guildId, IrcMessage message)
		{
			string target = message.arguments[0];
			string messageText = message.arguments[1];

			if (SERVER_DMS == guildId && target[0] == '#')
			{
				Log(new LogMessage(LogSeverity.Error, LogSource.IRC, $"Attempted to send message to guild channel {target} on the DM server."));
				if (ircServers.TryGetValue(guildId, out IrcServer server))
				{
					server.SendNotice("You cannot send a message to guild channels on the DM server.");
				}
			}
			else
			{
				switch (target[0])
				{
					case '#':
						{
							SocketTextChannel newChannel = discordClient.Guilds.Single(e => e.Id.ToString() == guildId).Channels.OfType<SocketTextChannel>().FirstOrDefault(e => "#" + e.Name.Sanitize() == target);
							if (newChannel != null)
							{
								if (ircServers.TryGetValue(guildId, out IrcServer server))
								{
									server.JoinGuildChannel(newChannel);
									server.SendNotice($"You were joined to guild channel {target} because you sent a message to it.", null, target);
									newChannel.SendMessageAsync(messageText);
								}
							}
							else
							{
								Log(LogSeverity.Warning, $"No guild channel named {target} exists.");
							}
							break;
						}
					case '&':
						{
							SocketGroupChannel newChannel = discordClient.PrivateChannels.OfType<SocketGroupChannel>().FirstOrDefault(e => "&" + e.Name.Sanitize() == target);
							if (newChannel != null)
							{
								if (ircServers.TryGetValue(SERVER_DMS, out IrcServer server))
								{
									server.JoinGroupChannel(newChannel);
									server.SendNotice($"You were joined to group dm {target} because you sent a message to it.", null, target);
									newChannel.SendMessageAsync(messageText);
								}
							}
							else
							{
								Log(LogSeverity.Warning, $"No group dm named {target} exists.");
							}
							break;
						}
					default:
						{
							string targetId = TryParseUserId(target, guildId);
							if (targetId != null)
							{
								if (discordClient.GetUser(ulong.Parse(targetId)).GetOrCreateDMChannelAsync().Result is SocketDMChannel newChannel)
								{
									if (ircServers.TryGetValue(SERVER_DMS, out IrcServer dmServer))
									{
										dmServer.JoinDMChannel(newChannel);
										newChannel.SendMessageAsync(messageText);
										if (ircServers.TryGetValue(guildId, out IrcServer guildServer))
										{
											guildServer.SendNotice($"Moved dm with {target} to DM server");
											guildServer.SendMessage(guildServer.config.clientHostmask, $"::dIRCd:: Moved dm with {target} to DM server", target);
										}
									}
								}
								else
								{
									Log(LogSeverity.Warning, $"Could not get or create dm chat to {target}.");
								}
							}
							break;
						}
				}
			}
		}

		internal string TryParseUserId(string hostmask, string guildId)
		{
			string userId = null;
			int index = hostmask.IndexOf('@');
			if (index >= 0)
			{
				userId = hostmask.Substring(index);
			}
			else if (guildId != SERVER_DMS)
			{
				userId = discordClient.Guilds.Single(e => e.Id == ulong.Parse(guildId)).Users.FirstOrDefault(u => u.IrcNick() == hostmask)?.Id.ToString();
			}
			else
			{
				userId = discordClient.Guilds.Select(e => e.Users.FirstOrDefault(u => u.IrcNick() == hostmask)).FirstOrDefault(u => u != null)?.Id.ToString();
			}

			if (userId == null)
			{
				Log(new LogMessage(LogSeverity.Error, LogSource.IRC, $"Failed to parse userId for irc user {hostmask}"));
			}

			return userId;
		}

		internal Task Log(LogSeverity severity, string message, Exception e = null)
		{
			return Log(new LogMessage(severity, LogSource.Discord, message, e));
		}

		internal Task Log(LogMessage message)
		{
			if (message.Severity <= bridgeConfig.logLevel)
			{
				output(string.Format("{0,-26}{1,-26}\t{2}", $"[{DateTime.Now}]", message.Source, message.Message));

				if (message.Exception != null)
				{
					output(message.Exception.StackTrace);
				}
			}
			return Task.CompletedTask;
		}
	}

	public struct BridgeConfig
	{
		public string token;
		public Dictionary<string, string> smileyMapping;
		public HashSet<ulong> excludedGuilds;
		public HashSet<ulong> excludedChannels;
		public LogSeverity logLevel;
	}

	public delegate void LogCallback(string message);
	struct LogSource { internal static string Discord = "dIRCd (Discord)", IRC = "dIRCd (IRC)"; }
}
