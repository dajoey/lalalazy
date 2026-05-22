using System;
using System.Collections.Generic;
using System.Linq;
using ECommons.DalamudServices;

namespace LazySightseeing;

public static class WeatherService
{
    // Eorzea multiplier (3600 Eorzea seconds in an hour / 175 Earth seconds in an hour)
    private const double EorzeaMultiplier = 3600D / 175D;
    private static readonly DateTime UnixEpoch = new(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public static DateTime GetEorzeaTime()
    {
        long epochTicks = DateTime.UtcNow.Ticks - UnixEpoch.Ticks;
        long eorzeaTicks = (long)Math.Round(epochTicks * EorzeaMultiplier);
        return new DateTime(eorzeaTicks, DateTimeKind.Utc);
    }

    public static int GetEorzeaHour()
    {
        return GetEorzeaTime().Hour;
    }

    public static string GetWeatherName(uint weatherId)
    {
        try
        {
            var sheet = Svc.Data.GetExcelSheet<Lumina.Excel.Sheets.Weather>();
            if (sheet == null) return "Unknown";
            var row = sheet.GetRow(weatherId);
            return row.Name.ToString() ?? "Unknown";
        }
        catch (Exception ex)
        {
            Svc.Log.Error(ex, $"Failed to get weather name for ID {weatherId}");
            return "Unknown";
        }
    }

    public static string GetCurrentWeatherName(uint territoryTypeId)
    {
        try
        {
            var territoryTypeSheet = Svc.Data.GetExcelSheet<Lumina.Excel.Sheets.TerritoryType>();
            if (territoryTypeSheet == null) return "Unknown";
            
            var territoryRow = territoryTypeSheet.GetRow(territoryTypeId);
            
            // Safe property access using reflection
            var rateProp = territoryRow.GetType().GetProperty("WeatherRate");
            if (rateProp == null) return "Unknown";
            
            var weatherRateId = Convert.ToUInt32(rateProp.GetValue(territoryRow));
            
            var weatherRateSheet = Svc.Data.GetExcelSheet<Lumina.Excel.Sheets.WeatherRate>();
            if (weatherRateSheet == null) return "Unknown";
            
            var weatherRateRow = weatherRateSheet.GetRow(weatherRateId);
            
            var targetValue = CalculateTarget(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            var activeWeatherId = GetWeatherIdFromRate(weatherRateRow, targetValue);
            
            return GetWeatherName(activeWeatherId);
        }
        catch (Exception ex)
        {
            Svc.Log.Error(ex, $"Failed to get current weather for territory {territoryTypeId}");
            return "Unknown";
        }
    }

    public static bool IsWeatherMatching(uint territoryTypeId, List<string> expectedWeathers)
    {
        if (expectedWeathers == null || expectedWeathers.Count == 0) return true;
        
        var current = GetCurrentWeatherName(territoryTypeId);
        return expectedWeathers.Contains(current, StringComparer.OrdinalIgnoreCase);
    }

    public static bool IsTimeInWindow(string window, int hour)
    {
        if (string.IsNullOrEmpty(window)) return true;
        
        try
        {
            var parts = window.Split('-');
            if (parts.Length != 2) return true;
            
            var startHour = int.Parse(parts[0].Split(':')[0]);
            var endHour = int.Parse(parts[1].Split(':')[0]);
            
            if (startHour <= endHour)
            {
                return hour >= startHour && hour < endHour;
            }
            else
            {
                // Wraps around midnight (e.g. 18:00 to 05:00)
                return hour >= startHour || hour < endHour;
            }
        }
        catch (Exception ex)
        {
            Svc.Log.Error(ex, $"Failed to parse time window: {window}");
            return true;
        }
    }

    public static int CalculateTarget(long unixSeconds)
    {
        long bell = unixSeconds / 175;
        uint increment = (uint)((bell + 8 - (bell % 8)) % 24);
        uint totalDays = (uint)(unixSeconds / 4200);
        uint calcBase = (totalDays * 100) + increment;
        uint step1 = (calcBase << 11) ^ calcBase;
        uint step2 = (step1 >> 8) ^ step1;
        return (int)(step2 % 100);
    }

    private static uint GetWeatherIdFromRate(Lumina.Excel.Sheets.WeatherRate weatherRateRow, int targetValue)
    {
        try
        {
            var properties = weatherRateRow.GetType().GetProperties();
            
            // Search for a property that is an array/enumerable of structs/objects ending in Struct0 or containing Struct
            var structArrayProp = properties.FirstOrDefault(p => p.Name.Contains("Struct0") || p.Name.Contains("Struct"));
            if (structArrayProp != null)
            {
                var arrayObj = structArrayProp.GetValue(weatherRateRow) as System.Collections.IEnumerable;
                if (arrayObj != null)
                {
                    int cumulativeRate = 0;
                    foreach (var item in arrayObj)
                    {
                        if (item == null) continue;
                        
                        var itemProps = item.GetType().GetProperties();
                        var weatherProp = itemProps.FirstOrDefault(p => p.Name.Equals("Weather", StringComparison.OrdinalIgnoreCase) || p.Name.Equals("WeatherId", StringComparison.OrdinalIgnoreCase));
                        var rateProp = itemProps.FirstOrDefault(p => p.Name.Equals("Rate", StringComparison.OrdinalIgnoreCase) || p.Name.Equals("Value", StringComparison.OrdinalIgnoreCase) || p.Name.Equals("Percentage", StringComparison.OrdinalIgnoreCase));
                        
                        if (weatherProp != null && rateProp != null)
                        {
                            var weatherVal = Convert.ToUInt32(weatherProp.GetValue(item));
                            var rateVal = Convert.ToInt32(rateProp.GetValue(item));
                            
                            cumulativeRate += rateVal;
                            if (targetValue < cumulativeRate)
                            {
                                return weatherVal;
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Svc.Log.Error(ex, "Failed to dynamically parse WeatherRate row");
        }
        
        return 0; // Fallback
    }
}
