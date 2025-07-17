using System.Text.Json;

namespace RoWheelBridge;

public class WheelCalibration
{
    public int ThrottleAxis { get; set; } = -1;
    public int BrakeAxis { get; set; } = -1;
    public int ClutchAxis { get; set; } = -1;
    public int SteeringAxis { get; set; } = -1;
    
    public int ThrottleMin { get; set; }
    public int ThrottleMax { get; set; }
    public int BrakeMin { get; set; }
    public int BrakeMax { get; set; }
    public int ClutchMin { get; set; }
    public int ClutchMax { get; set; }
    public int SteeringMin { get; set; }
    public int SteeringMax { get; set; }
    
    public int ShiftUpButton { get; set; } = -1;
    public int ShiftDownButton { get; set; } = -1;
    
    public void SaveToFile(string filename)
    {
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(filename, json);
    }
    
    public static WheelCalibration LoadFromFile(string filename)
    {
        if (!File.Exists(filename))
            return new WheelCalibration();
            
        var json = File.ReadAllText(filename);
        return JsonSerializer.Deserialize<WheelCalibration>(json) ?? new WheelCalibration();
    }
}