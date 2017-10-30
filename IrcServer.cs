using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;

namespace dIRCd
{
	internal class IrcServer
	{
		internal Dictionary<string, SocketChannel> channels = new Dictionary<string, SocketChannel>();
		private IPAddress address;
		private int port;
		private TcpListener listener;
		private TcpClient client;
		private StreamReader reader;
		private StreamWriter writer;
		internal IrcServerConfig config;
		private CancellationTokenSource canceller = new CancellationTokenSource();
		internal readonly BridgeServer bridge;
		private Queue<string> lastMessageHashes = new Queue<string>();
		private Timer pingIntervalTimer, pingResponseTimer;
		private string lastPing = null;
		private ImmutableDictionary<string, SocketTextChannel> GuildChannels => channels.Where(e => e.Value is SocketTextChannel).ToImmutableDictionary(e => e.Key, e=> e.Value as SocketTextChannel);
		private ImmutableDictionary<string, SocketDMChannel> DMChannels => channels.Where(e => e.Value is SocketDMChannel).ToImmutableDictionary(e => e.Key, e => e.Value as SocketDMChannel);
		private ImmutableDictionary<string, SocketGroupChannel> GroupChannels => channels.Where(e => e.Value is SocketGroupChannel).ToImmutableDictionary(e => e.Key, e => e.Value as SocketGroupChannel);
		private string CurrentNick => bridge.GetOwnUser(config.guildId).IrcNick();
		private string CurrentHostname => bridge.GetOwnUser(config.guildId).IrcHostname();

		internal IrcServer(BridgeServer bridge, IPAddress address, int port, IrcServerConfig config)
		{
			this.bridge = bridge;
			this.address = address;
			this.port = port;
			this.config = config;
		}

		internal async Task Run(IEnumerable<IChannel> serverChannels)
		{
			try
			{
				listener = new TcpListener(address, port);
				listener.Start();
				Log(LogSeverity.Info, $"IRC Server for guild {config.guildName} ({config.guildId}) started on port {port}");

				while (!canceller.Token.IsCancellationRequested)
				{
					Log(LogSeverity.Info, $"Waiting for new Client connection for {config.guildName}");
					client = await listener.AcceptTcpClientAsync();
					Log(LogSeverity.Info, $"Client connection initiated for {config.guildName}");
					reader = new StreamReader(client.GetStream(), Encoding.UTF8);
					writer = new StreamWriter(client.GetStream(), Encoding.UTF8)
					{
						AutoFlush = true,
						NewLine = "\r\n"
					};

					pingIntervalTimer = new Timer(e => Ping(), null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60));
					pingResponseTimer = new Timer(e => PingTimeout());

					foreach (SocketChannel channel in serverChannels)
					{
						if (channel is SocketTextChannel textChannel)
						{
							channels["#" + textChannel.Name.Sanitize()] = textChannel;
							Log(LogSeverity.Info, $"Adding guild channel #{textChannel.Name} ({textChannel.Id})");
						}
						else if (channel is SocketDMChannel dmChannel)
						{
							channels[dmChannel.Recipient.Id.ToString()] = dmChannel;
							Log(LogSeverity.Info, $"Adding DM chat with #{dmChannel.Recipient.IrcNick()} ({dmChannel.Id})");
						}
						else if (channel is SocketGroupChannel groupChannel)
						{
							channels["&" + groupChannel.Name] = groupChannel;
							Log(LogSeverity.Info, $"Adding group channel &{groupChannel.Name} ({groupChannel.Id})");
						}
						else if (channel is SocketGuildChannel guildChannel)
						{
							Log(LogSeverity.Debug, $"Skipping channel {guildChannel.Name} ({guildChannel.Id}) as it is not a text channel");
						}
						else
						{
							Log(LogSeverity.Debug, $"Skipping channel ({channel.Id}) as it is not a guild channel");
						}
					}

					try
					{
						while (!canceller.IsCancellationRequested && client.Connected && (await DataAvailable(reader)))
						{
							string readLine = await reader.ReadLineAsync();

							if (readLine != null)
							{
								try
								{
									await ProcessIrcLine(readLine);
								}
								catch (ArgumentException e)
								{
									Log(LogSeverity.Critical, $"{e.Message}\n{e.StackTrace}");
								}
							}
						}
					}
					catch (IOException)
					{
						// TCP connection was aborted, just restart listening
					}
					finally
					{
						Log(LogSeverity.Info, $"Client connection died for {config.guildName}");

						CleanupConnection();
					}
				}
			}
			catch (ObjectDisposedException)
			{
				// We shut down while awaiting a connection, just keep closing things down
			}
			catch (Exception e)
			{
				Log(LogSeverity.Critical, $"Unrecoverable {e}: {e.Message}\n{e.StackTrace}");
			}
			finally
			{
				CleanupConnection();
				listener.Stop();
				bridge.ircServers.Remove(config.guildId);
				Log(LogSeverity.Info, $"IRC server for {config.guildName} shut down.");
			}
		}

		private Task<bool> DataAvailable(StreamReader reader)
		{
			try
			{
				return Task.FromResult(!reader.EndOfStream);
			}
			catch (IOException)
			{
				// The reader got killed while attempting to read, just return that there's no more data
				return Task.FromResult(false);
			}
		}

		internal Task Shutdown()
		{
			Log(LogSeverity.Info, $"IRC server for {config.guildName} shutting down...");
			canceller.Cancel();
			CleanupConnection();
			listener.Stop();
			return Task.CompletedTask;
		}

		private void CleanupConnection()
		{
			pingResponseTimer?.Dispose();
			pingIntervalTimer?.Dispose();
			writer?.Dispose();
			reader?.Dispose();
			client?.Close();
		}

		#region Basic message receiving/sending

		private Task ProcessIrcLine(string readLine)
		{
			IrcMessage message = new IrcMessage
			{
				source = null,
				command = null,
				arguments = new List<string>(),
				trailing = false
			};
			int searchIndex = 0;
			int nextIndex = 0;

			// Check for optional SOURCE parameter
			if (readLine.StartsWith(":"))
			{
				nextIndex = readLine.IndexOf(" ", searchIndex);
				message.source = readLine.Substring(1, nextIndex - 1);
				searchIndex = nextIndex + 1;
			}

			// Get COMMAND parameter
			nextIndex = readLine.IndexOf(" ", searchIndex);
			if (nextIndex > searchIndex)
			{
				message.command = readLine.Substring(searchIndex, nextIndex - searchIndex);
				searchIndex = nextIndex + 1;
			}
			else
			{
				return Task.FromException(new ArgumentException($"Malformed message read from IRC ({readLine}): Command name expected at index {nextIndex}"));
			}

			// Get space-separated ARGUMENTS parameters
			while ((searchIndex < readLine.Length) && ((nextIndex = readLine.IndexOf(" ", searchIndex)) >= searchIndex))
			{
				if (nextIndex == searchIndex)
				{
					searchIndex++;
					continue;
				}
				else
				{
					string arg = readLine.Substring(searchIndex, nextIndex - searchIndex);
					if (arg.StartsWith(":"))
					{
						message.trailing = true;
						break;
					}
					else
					{
						message.arguments.Add(arg);
						searchIndex = nextIndex + 1;
					}
				}
			}

			// Skip trailing argument indicator if present
			if (readLine[searchIndex] == ':')
			{
				searchIndex++;
			}
			// Get last ARGUMENT
			if (searchIndex < readLine.Length)
			{
				string lastArg = readLine.Substring(searchIndex);
				if (lastArg.Length > 0)
				{
					message.arguments.Add(lastArg);
				}
			}

			return ProcessMessage(message);
		}

		private Task ProcessMessage(IrcMessage message)
		{
			Log(message.command == "PING" || message.command == "PONG" ? LogSeverity.Debug : LogSeverity.Info, $"< {config.serverName} {message}");
			RefreshPingTimer();
			switch (message.command)
			{
				case "NICK":
					if (!ValidateArgCount(message, 1))
					{
						break;
					}
					config.clientHostmask = message.arguments[0];
					break;
				case "USER":
					SendHandshake();
					SendSelfNickchange(CurrentNick);
					ForceJoinChannels();
					break;
				case "PING":
					if (!ValidateArgCount(message, 1))
					{
						break;
					}
					SendPong(message.arguments[0]);
					break;
				case "PONG":
					if (!ValidateArgCount(message, 1))
					{
						break;
					}
					if (message.arguments[0] == lastPing)
					{
						SetPingResponseTimer(Timeout.InfiniteTimeSpan);
						lastPing = null;
					}
					else
					{
						Log(LogSeverity.Warning, $"Ping response did not match up: {message.arguments[0]}, {lastPing}");
					}
					break;
				case "PRIVMSG":
				case "NOTICE":
					if (!ValidateArgCount(message, 2))
					{
						break;
					}
					string target = message.arguments[0];
					string messageText = message.arguments[1];

					if (target[0] == '#' || target[0] == '&')
					{
						if (channels.TryGetValue(target, out SocketChannel channel))
						{
							if (channel is SocketTextChannel guildChannel)
							{
								guildChannel.SendMessageAsync(messageText);
								AddOwnMessage(messageText);
							}
							else if (channel is SocketGroupChannel groupChannel)
							{
								groupChannel.SendMessageAsync(messageText);
								AddOwnMessage(messageText);
							}
							else
							{
								Log(LogSeverity.Critical, $"I have no idea how you sent to channel {channel.Id}, but you shouldn't be able to.");
							}
						}
						else
						{
							bridge.UnknownChannelMessage(config.guildId, message);
						}
					}
					else
					{
						string targetId = bridge.TryParseUserId(target, config.guildId);
						if (targetId != null)
						{
							if (channels.TryGetValue(targetId, out SocketChannel channel))
							{
								if (channel is SocketDMChannel dmChannel)
								{
									dmChannel.SendMessageAsync(messageText);
									AddOwnMessage(messageText);
								}
							}
							else
							{
								bridge.UnknownChannelMessage(config.guildId, message);
							}
						}
					}
					break;
				case "MODE":
					break;
				case "CAP":
				case "USERHOST":
					//we're ignoring these
					break;
				default:
					Log(LogSeverity.Warning, $"Unknown command [{message.command}].");
					break;
			}

			return Task.CompletedTask;
		}

		private bool ValidateArgCount(IrcMessage message, int count)
		{
			if (message.arguments.Count == count)
			{
				return true;
			}
			else
			{
				Log(LogSeverity.Error, $"Invalid argument count ({message.arguments.Count}) for command [{message.command}] (takes {count}).");
				return false;
			}
		}

		private bool ValidateArgMin(IrcMessage message, int count)
		{
			if (message.arguments.Count >= count)
			{
				return true;
			}
			else
			{
				Log(LogSeverity.Error, $"Insufficient argument count ({message.arguments.Count}) for command [{message.command}] (requires at least {count}).");
				return false;
			}
		}

		private void SendIrcMessage(IrcMessage ircMessage, bool replaceNullSender = true, LogSeverity severity = LogSeverity.Info)
		{
			Log(severity, $"> {config.serverName} {ircMessage}");
			RefreshPingTimer();

			if (replaceNullSender)
			{
				ircMessage.source = ircMessage.source ?? config.serverName;
			}
			writer?.WriteLine(ircMessage);
		}

		#endregion

		#region IRC message types

		internal void SendMessage(string channel, string message, string author = null)
		{
			SendIrcMessage(new IrcMessage("PRIVMSG", new List<String> { channel, message }, author ?? config.clientHostmask));
		}

		internal void SendNotice(string message, string source = null, string target = null)
		{
			SendIrcMessage(new IrcMessage("NOTICE", new List<String> { config.clientHostmask, message }, source ?? config.serverName));
		}

		private void SendHandshake()
		{
			string clientNick = config.clientHostmask ?? String.Empty;
			string serverName = config.serverName;
			DateTime startTime = config.startTime;
			System.Version appVersion = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;

			SendIrcMessage(new IrcMessage(IrcNumerics.Welcome, new List<string> { clientNick, $"Welcome to your Discord IRC Bridge, {clientNick}" }));
			SendIrcMessage(new IrcMessage(IrcNumerics.Host, new List<string> { clientNick, $"Your host is dIRCd, version {appVersion}" }));
			SendIrcMessage(new IrcMessage(IrcNumerics.Created, new List<string> { clientNick, $"Server has been running for {DateTime.Now - startTime}, since {startTime}" }));
			SendIrcMessage(new IrcMessage(IrcNumerics.Info, new List<string> { clientNick, $"{serverName} dIRCd{appVersion} {String.Empty} {String.Empty}"}, trailing: false));
		}

		private void SendPing(string timestamp)
		{
			SendIrcMessage(new IrcMessage("PING", timestamp), false, LogSeverity.Debug);
		}

		private void SendPong(string reply)
		{
			SendIrcMessage(new IrcMessage("PONG", reply), false, LogSeverity.Debug);
		}

		private void SendJoin(string channel, string user)
		{
			SendIrcMessage(new IrcMessage("JOIN", channel, user));
		}

		private void SendQuit(string user)
		{
			SendIrcMessage(new IrcMessage("QUIT", "User left server", user));
		}

		private void SendTopic(string channel, string topic)
		{
			SendIrcMessage(new IrcMessage(IrcNumerics.Topic, new List<string> { config.clientHostmask, channel, topic }));
		}

		private void SendNames(string channel, string names)
		{
			SendIrcMessage(new IrcMessage(IrcNumerics.Names, new List<string> { config.clientHostmask, "=", channel, names }));
			SendIrcMessage(new IrcMessage(IrcNumerics.EndNames, new List<string> { config.clientHostmask, channel, "End of NAMES list" }));
		}

		private void SendNickchange(string hostname, string newNick)
		{
			SendIrcMessage(new IrcMessage("NICK", newNick, hostname));
		}

		#endregion

		private void ForceJoinChannels()
		{
			foreach (KeyValuePair<string, SocketTextChannel> entry in GuildChannels.ToList().OrderBy(e => e.Value.Position))
			{
				string channelName = entry.Key;
				SocketTextChannel discordChannel = entry.Value;

				SendJoin(channelName, config.clientHostmask);
				SendTopic(channelName, discordChannel.Topic);
				SendNames(channelName, discordChannel.Users.Select(e => (e.IrcHostname())).Aggregate((a, v) => a + " " + v));
			}

			foreach (KeyValuePair<string, SocketGroupChannel> entry in GroupChannels.ToList().OrderBy(e => e.Value.CreatedAt))
			{
				string channelName = entry.Key;
				SocketGroupChannel discordChannel = entry.Value;

				SendJoin(channelName, config.clientHostmask);
				SendNames(channelName, discordChannel.Users.Select(e => (e.IrcHostname())).Aggregate((a, v) => a + " " + v));
			}
		}

		internal void JoinGuildChannel(SocketTextChannel discordChannel)
		{
			string channelName = "#" + discordChannel.Name;
			if (!channels.ContainsKey(channelName))
			{
				channels[channelName] = discordChannel;
				SendJoin(channelName, config.clientHostmask);
				SendTopic(channelName, discordChannel.Topic);
				SendNames(channelName, discordChannel.Users.Select(e => (e.IrcHostname())).Aggregate((a, v) => a + " " + v));
			}
		}

		internal void JoinGroupChannel(SocketGroupChannel discordChannel)
		{
			string channelName = "&" + discordChannel.Name;
			if (!channels.ContainsKey(channelName))
			{
				channels[channelName] = discordChannel;
				SendJoin(channelName, config.clientHostmask);
				SendNames(channelName, discordChannel.Users.Select(e => (e.IrcHostname())).Aggregate((a, v) => a + " " + v));
			}
		}

		internal void JoinDMChannel(SocketDMChannel newChannel)
		{
			string channelName = newChannel.Recipient.Id.ToString();
			if (!channels.ContainsKey(channelName))
			{
				channels[channelName] = newChannel;
			}
		}

		internal void SendUserJoin(SocketGuildUser user)
		{
			foreach (KeyValuePair<string, SocketTextChannel> channel in GuildChannels)
			{
				if (channel.Value.Users.Contains(user))
				{
					SendJoin(channel.Key, user.IrcHostname());
				}
			}
		}

		internal void SendUserQuit(SocketGuildUser user)
		{
			SendQuit(user.IrcHostname());
		}

		internal void SendUserNickchange(string hostname, SocketUser newUser)
		{
			SendNickchange(hostname, newUser.IrcNick());
			if (channels.ContainsKey(hostname))
			{
				channels[newUser.IrcHostname()] = channels[hostname];
				channels.Remove(hostname);
			}
		}

		internal void SendSelfNickchange(string newNick)
		{
			SendNickchange(config.clientHostmask, newNick);
			config.clientHostmask = CurrentHostname;
		}

		#region Utility & housekeeping

		internal bool IsOwnMessage(string message)
		{
			if (lastMessageHashes.Count > 0 && message.Md5Hash() == lastMessageHashes.Peek())
			{
				lastMessageHashes.Dequeue();
				return true;
			}
			else
			{
				return false;
			}
		}

		internal void AddOwnMessage(string message)
		{
			lastMessageHashes.Enqueue(message.Md5Hash());
		}

		private void Ping()
		{
			if (lastPing != null)
			{
				PingTimeout();
			}
			else
			{
				lastPing = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
				SetPingResponseTimer(TimeSpan.FromSeconds(10));
				SendPing(lastPing);
			}
		}

		private void RefreshPingTimer()
		{
			pingIntervalTimer?.Change(TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60));
		}

		private void SetPingResponseTimer(TimeSpan time)
		{
			pingResponseTimer?.Change(time, Timeout.InfiniteTimeSpan);
		}

		private void PingTimeout()
		{
			throw new IOException("Ping timeout");
		}

		private void Log(LogSeverity severity, string message)
		{
			bridge.Log(new LogMessage(severity, LogSource.IRC, message));
		}

		#endregion
	}

	internal struct IrcServerConfig
	{
		internal string serverName;
		internal string clientHostmask;
		internal string guildId;
		internal string guildName;
		internal DateTime startTime;
	}

	internal struct IrcMessage
	{
		internal string source;
		internal string command;
		internal List<string> arguments;
		internal bool trailing;

		internal IrcMessage(string command, List<string> arguments, string source = null, bool trailing = true)
		{
			this.source = source;
			this.command = command;
			this.arguments = arguments.ConvertAll(e => e ?? string.Empty);
			this.trailing = this.arguments.Last().Contains(" ") || trailing;
		}

		internal IrcMessage(string command, string argument, string source = null, bool trailing = true) : this(command, new List<string> { argument }, source, trailing)
		{
		}

		public override string ToString()
		{
			/****************************
			 * ABANDON ALL HOPE etc etc *
			 * ------------------------ *
			 *  This method does some   *
			 * heavy lifting. It really *
			 *       isn't pretty.      *
			 ****************************/
			StringBuilder messageBuilder = new StringBuilder();
			if (source != null)
			{
				messageBuilder.Append(":" + source + " ");
			}

			messageBuilder.Append(command);

			if (arguments.Count > 0)
			{
				for (int i = 0; i < arguments.Count - 1; i++)
				{
					messageBuilder.Append(" ");
					messageBuilder.Append(arguments[i]);
				}

				//Special handling for the last argument, since that's almost always going to be the longest one, or nonexistent
				if (!trailing)
				{
					messageBuilder.Append(" ");
					messageBuilder.Append(arguments[arguments.Count - 1]);
				}
				else
				{
					messageBuilder.Append(" :");
					string fullMessage = arguments[arguments.Count - 1];
					string messageBase = messageBuilder.ToString();
					int baseLength = Encoding.UTF8.GetByteCount(messageBase);
					int maxArgumentsLength = 510 - baseLength;
					List<string> splitLines = fullMessage.Split( new string[] {"\r\n", "\n", "\r"}, StringSplitOptions.RemoveEmptyEntries).ToList();

					if (splitLines.Count == 1 && Encoding.UTF8.GetByteCount(splitLines[0]) <= maxArgumentsLength)
					{
						messageBuilder.Append(splitLines[0]);
					}
					else
					{
						messageBuilder.Clear();

						for (int i = 0; i < splitLines.Count; i++)
						{
							if (splitLines[i].Length <= maxArgumentsLength)
							{
								continue;
							}
							else
							{
								int index = i;
								string fullLine = splitLines[index];

								splitLines.RemoveAt(index);

								int lineLength = 0;
								StringBuilder lineBuilder = new StringBuilder();
								foreach (string word in fullLine.Split((char[]) null, StringSplitOptions.RemoveEmptyEntries))
								{
									int wordLength = Encoding.UTF8.GetByteCount(word);
									if (lineLength + wordLength < maxArgumentsLength)
									{
										if (lineBuilder.Length > 0)
										{
											lineBuilder.Append(" ");
											wordLength += 1;
										}
										lineBuilder.Append(word);
										lineLength += wordLength;
									}
									else
									{
										splitLines.Insert(index, lineBuilder.ToString());
										lineBuilder.Clear();
										index += 1;

										if (wordLength < maxArgumentsLength)
										{
											lineBuilder.Append(word);
											lineLength = wordLength;
										}
										else
										{
											StringInfo wordRest = new StringInfo(word);
											do
											{
												int wordIndex = Math.Min(wordRest.LengthInTextElements, maxArgumentsLength);
												string front = wordRest.SubstringByTextElements(0, wordIndex);
												while (Encoding.UTF8.GetByteCount(front) > maxArgumentsLength)
												{
													wordIndex -= 10;
													front = wordRest.SubstringByTextElements(0, wordIndex);
												}
											
												lineBuilder.Append(front);

												if (wordRest.LengthInTextElements > wordIndex)
												{
													splitLines.Insert(index, lineBuilder.ToString());
													lineBuilder.Clear();
													index += 1;
													wordRest.String = wordRest.SubstringByTextElements(wordIndex);
												}
												else
												{
													lineLength = Encoding.UTF8.GetByteCount(front);
													wordRest.String = String.Empty;
												}
											}
											while (wordRest.LengthInTextElements > 0);
										}
									}
								}
								splitLines.Insert(index, lineBuilder.ToString());
							}
						}

						for (int i = 0; i < splitLines.Count; i++)
						{
							messageBuilder.Append(messageBase);
							messageBuilder.Append(splitLines[i]);
							if (i < splitLines.Count - 1)
							{
								messageBuilder.Append("\r\n");
							}
						}
					}
				}
			}

			return messageBuilder.ToString();

		}
	}

	struct IrcNumerics { internal static string Welcome = "001", Host = "002", Created = "003", Info = "004", Topic = "332", Names = "353", EndNames = "366"; };
}
