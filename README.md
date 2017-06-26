CiviBotti

CiviBotti can found be searching for civ_gmr_bot in telegram, this is just its source code

This bot is for sending notifications (and general spam and other relevant and not so relevant messages) from giant multiplayer robot games to telegram chats.

Basic features
-Turn notifications, sends notifications when player has a new turn
-Display turn order
-Send text to speech info on request
-saves data either to sqlite or sql database

Planed features
-notify next player when current player launches game
-post gifs, images and other funny related stuff
-display turn timers
-more random string
-customisation


Building and running
-open with visual studio
-restore all nuget packages
-create a file bot.config and set all the configurations you want, example is provided
-build and run

Using the bot
-Find the bot by searching for civ_gmr_bot (or your bot if you are building exe yourself)
-You need to register your telegram user with your giant multiplayer robot, type in '/register authenticationkey' to bot in private (authentication key can be found here: http://multiplayerrobot.com/Download)
-you need to first create a game before you can add it to chat, type in '/newgame id' where id is the game id (can be found from url of the game)
-Add the game to chat by first adding the bot in (if its a group chat) and then sending '/addgame id' (same as /newgame id)
-you can add the game to as many channels as you want, and in private chats, however you can only have one game per channel.
-Bot will now start sending messages in the chat, other users should register themself if they want to be pinged in telegram, otherwise their steam name will be used to display.


Commands
/order - displays turn order
/tee - tells the current player in tts to do their turn (in finish at the moment)
/next - displays next player
/removegame removes a game from the chat
