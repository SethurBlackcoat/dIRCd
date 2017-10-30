using System.Security.Cryptography;
using System.Text;
using Discord.WebSocket;

namespace dIRCd
{
	public static class Extensions
	{
		private static MD5CryptoServiceProvider md5 = new MD5CryptoServiceProvider();

		internal static string Sanitize(this string input)
		{ 
			return input?.Replace(" ", "_").Replace("!", "").Replace("@", "");
		}

		internal static string IrcHostname(this SocketUser user)
		{
			if (user is SocketGuildUser guildUser)
			{
				return user.IrcNick() + "!DiscordUser@" + guildUser.Id.ToString();
			}
			else
			{
				return user.Username.Sanitize() + "!DiscordUser@" + user.Id.ToString();
			}
		}

		internal static string IrcNick(this SocketUser user)
		{
			/*if (user is SocketGuildUser guildUser)
			{
				return (guildUser.Nickname ?? guildUser.Username).Sanitize();
			}
			else*/
			{
				return user.Username.Sanitize();
			}
		}

		internal static string Md5Hash(this string value)
		{
			StringBuilder builder = new StringBuilder(32);
			foreach (byte b in md5.ComputeHash(Encoding.UTF8.GetBytes(value)))
			{
				builder.Append(b.ToString("x2"));
			}

			return builder.ToString();
		}
	}
}
