using System.IO;
using UnityEngine;

/// 每台車的最佳成績。JSON 存在 persistentDataPath，不進版控。
[System.Serializable]
public class CarRecord
{
    public float bestLap = -1f;
    public float bestScore = 0f;
    public float bestCombo = 0f;
    public int laps = 0;
}

[System.Serializable]
public class SaveData
{
    public CarRecord[] cars = new CarRecord[0];

    public CarRecord For(int index)
    {
        if (index < 0) index = 0;
        if (cars.Length <= index)
        {
            var grown = new CarRecord[index + 1];
            for (int i = 0; i < grown.Length; i++)
                grown[i] = i < cars.Length && cars[i] != null ? cars[i] : new CarRecord();
            cars = grown;
        }
        if (cars[index] == null) cars[index] = new CarRecord();
        return cars[index];
    }
}

public static class SaveSystem
{
    static SaveData cached;

    static string Path => System.IO.Path.Combine(Application.persistentDataPath, "driftgame_save.json");

    public static SaveData Data
    {
        get
        {
            if (cached == null) cached = Load();
            return cached;
        }
    }

    static SaveData Load()
    {
        try
        {
            if (File.Exists(Path))
            {
                var json = File.ReadAllText(Path);
                var data = JsonUtility.FromJson<SaveData>(json);
                if (data != null) return data;
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[SaveSystem] 讀檔失敗，改用空白存檔：" + e.Message);
        }
        return new SaveData();
    }

    public static void Save()
    {
        try
        {
            File.WriteAllText(Path, JsonUtility.ToJson(Data, true));
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[SaveSystem] 存檔失敗：" + e.Message);
        }
    }

    /// 回報一圈成績，回傳這圈刷新了哪些紀錄。
    public static void Submit(int carIndex, float lapTime, float score, float combo,
                              out bool newLap, out bool newScore, out bool newCombo)
    {
        var rec = Data.For(carIndex);
        newLap = lapTime > 0f && (rec.bestLap < 0f || lapTime < rec.bestLap);
        newScore = score > rec.bestScore;
        newCombo = combo > rec.bestCombo;

        if (newLap) rec.bestLap = lapTime;
        if (newScore) rec.bestScore = score;
        if (newCombo) rec.bestCombo = combo;
        rec.laps++;
        Save();
    }
}
