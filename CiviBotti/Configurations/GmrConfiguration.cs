namespace CiviBotti.Configurations;

using Services;
using Microsoft.Extensions.Configuration;

public class GmrConfiguration
{
    public static readonly string Configuration = "GMR";
    
    [ConfigurationKeyName("URL")]  
    public required string GmrUrl { get; set; }
}