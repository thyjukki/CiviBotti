namespace CiviBotti.Configurations;

using Services;
using Microsoft.Extensions.Configuration;

public class BotConfiguration
{
    public static readonly string Configuration = "CiviBotti";

    [ConfigurationKeyName("BOT_TOKEN")]  
    public required string BotToken { get; set; }
    
    [ConfigurationKeyName("GMR_URL")]  
    public required string GmrUrl { get; set; }
    
    [ConfigurationKeyName("SPEECH_KEY")]  
    
    public required string SpeechKey { get; set; }
    
    [ConfigurationKeyName("SPEECH_REGION")]  
    public required string SpeechRegion { get; set; }
    
    [ConfigurationKeyName("DB_TYPE")]  
    public required Database.DatabaseType DbType { get; set; }
    
    [ConfigurationKeyName("DB_HOST")]  
    public required string Host { get; set; }
    
    [ConfigurationKeyName("DB_USER")]  
    public required string User { get; set; }
    
    [ConfigurationKeyName("DB_PW")]  
    public required string Password { get; set; }
    
    [ConfigurationKeyName("DB_NAME")]  
    public required string Database { get; set; }
    
    [ConfigurationKeyName("STEAM_API_URL")] 

    public required string SteamApiUrl { get; set; }
    
    [ConfigurationKeyName("STEAM_API_KEY")] 
    public required string SteamApiKey { get; set; }
}