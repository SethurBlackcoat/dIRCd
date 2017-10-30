# dIRCd
A Discord connection endpoint for IRC clients  

### why tho
Because I still have people using IRC and I'm not dealing with multiple clients. You can't make Discord connect to an IRC server, so...

### I found a bug / X doesn't work
I know. This isn't anywhere near done yet. Super basic functionality is here:
* One IRC server per Discord server/guild that you've joined.
	* Channel messages are passed back and forth as you'd expect. If you write something in some other Discord client (like the mobile one) it'll show up in IRC too. 
	* You join all text channels on a server by default.
		* You can exclude entire servers or only specific channels via the config file. 
	* All names right now are the Discord-wide Username, sanitized to remove some special characters.
		* Server-specific Nicknames aren't being used yet because those often include emojis and I don't handle those yet and it looks shit.
		* Your name in IRC will be set to whatever your Discord name is on connect.
* A separate server for (group) DMs, since those are server-agnostic in Discord.
	* If you try and send a DM from a different server, it'll be migrated over to the DM server.
	* DMs with people that you're not on a server with don't work yet.
* Basic handling of mentions, smileys and attachments. So far only one-way, i.e. Discord formatting is turned plaintext for IRC.

### Here's a bunch of examples of fun things that are still completely broken
* If two people have the same username, I don't know what's going to happen. They'll have a different hostmask in IRC but I have no idea how IRC clients would handle nick collisions.
* Group DMs are entirely untested. The code's there, but I have no idea what's going to happen.
* Currently only works for Discord servers you've joined at the time you start dIRCd. If you join a new one, you'll have to restart dIRCd for it to show up there. Same for any changes to channels (creation/deletion/etc.)
* Basically all IRC commands that aren't sending a message aren't implemented. This means things like user modes, changing your own nick, joining/parting channels, whatever.
* I just pass through all incoming text as UTF8 messages. If your client and font can handle that, great! Otherwise you're going to see a whole bunch of empty boxes when people use emojis.
* You probably shouldn't use this to connect to servers with lots of members (more than a few hundred). It'll work, but your IRC client will probably lock up for a while on connect because IRC really wasn't made for that.

### Will any of this ever get fixed?
I dunno but I'll try to get around to it. Soon. Where "soon" is a variable length of time that references anything from tomorrow to never.

### If you really want to run this yourself, here's how
1. Build the sucker.
1. Make sure the config.json is in the same directory as the executable.
1. Put your token in the "token" field. You'll have to get it from your browser's local storage or similar. Instructions for how to do this can be found on google, for example https://anidiotsguide.gitbooks.io/discord-js-bot-guide/examples/selfbots-are-awesome.html#the-token
	* **You're trying to steal my token and impersonate me!**
		* No. You're an idiot. You can literally look at the code and see what I do with it.
	* This is annoying and not very user friendly.
		* Yes. I'll try and improve this in the future. For now, deal with it.
1. If you want certain custom guild smilies to not just show up as their name (i.e. :smileyname:), you can set up a replacement in the "smileyMapping" field, like the examples provided.
1. If you want to not join certain servers or channels, put their IDs in the "excludedGuilds" and "excludedChannels" fields, like the examples provided.
1. The "loglevel" field controls how much log output you see in the output window. I recommend leaving it at 3 for now, but if you want to cut down on it, the levels are from least to most output:
	* 0 - Critical
	* 1 - Error
	* 2 - Warning
	* 3 - Info
	* 4 - Verbose
	* 5 - Debug
1. Run it. You'll automatically connect to Discord and IRC servers will start under 127.0.0.1, with ports starting at 6699 (for the DM server) and going upwards. You can see which port is for which guild in the output window. They'll be asigned in ascending order of when you joined that Discord server.
	* You should really be able to set a starting port yourself. I'll add that. But I forgot to do it before writing this and right now I'm too lazy to fix it before committing.
1. Connect to the servers with any IRC client. It should work. Probably. I've only tested it with mIRC, honestly.
1. Enjoy the rotten fruit of my tilting at windmills.

### Your code is ugly
So's your face.  
  
The code is too, though. I hacked this together over a few nights, mostly way past when I should have been in bed. You should've seen it before the two major refactorings it already underwent (yes, seriously). I'll fix it eventually. Probably. Maybe. Soon.

## Dependencies
Get these from NuGet. Or somewhere else. Or not at all. I don't care. It won't work without 'em though.
* Discord.Net.Core
* Discord.Net.Rest
* Discord.Net.WebSocket
* Discord.Net.Providers.WS4Net
* WebSocket4Net
	* Be sure not to get version 0.15 of this, it'll break everything. Stick with 0.14.1. Or maybe they've fixed it by now, but don't blame me if you try and it doesn't work.
* Newtonsoft.Json
