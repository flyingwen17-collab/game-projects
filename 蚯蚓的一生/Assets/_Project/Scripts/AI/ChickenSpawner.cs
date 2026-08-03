using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// 雞生成器：照時間表放出不同種類的雞，難度隨時間上升
public class ChickenSpawner : MonoBehaviour
{
    public int maxCount = 9;
    public float minDistFromWorm = 10f;
    public Vector2 areaHalf = new Vector2(17f, 17f);

    struct Wave { public float time; public ChickenBody.Kind kind; public int count; }

    static readonly Wave[] Schedule =
    {
        new Wave { time = 0f,   kind = ChickenBody.Kind.Hen,     count = 2 },
        new Wave { time = 30f,  kind = ChickenBody.Kind.Hen,     count = 1 },
        new Wave { time = 60f,  kind = ChickenBody.Kind.Chick,   count = 3 },
        new Wave { time = 90f,  kind = ChickenBody.Kind.Rooster, count = 1 },
        new Wave { time = 130f, kind = ChickenBody.Kind.Hen,     count = 1 },
        new Wave { time = 170f, kind = ChickenBody.Kind.Rooster, count = 1 },
    };

    readonly List<GameObject> chickens = new List<GameObject>();
    int nextWave;
    float lateSpawnTimer;
    Transform worm;

    void Start()
    {
        var w = GameObject.FindGameObjectWithTag("Player");
        if (w != null) worm = w.transform;
    }

    void Update()
    {
        if (GameManager.I == null || GameManager.I.State != GameState.Playing) return;

        chickens.RemoveAll(c => c == null);
        float t = GameManager.I.SurvivalTime;

        // 照時間表出雞
        while (nextWave < Schedule.Length && t >= Schedule[nextWave].time)
        {
            for (int i = 0; i < Schedule[nextWave].count; i++)
                if (chickens.Count < maxCount) Spawn(Schedule[nextWave].kind);
            nextWave++;
        }

        // 時間表跑完之後：每 40 秒隨機補一隻
        if (nextWave >= Schedule.Length)
        {
            lateSpawnTimer += Time.deltaTime;
            if (lateSpawnTimer >= 40f && chickens.Count < maxCount)
            {
                lateSpawnTimer = 0f;
                var kinds = new[] { ChickenBody.Kind.Hen, ChickenBody.Kind.Chick, ChickenBody.Kind.Rooster };
                Spawn(kinds[Random.Range(0, kinds.Length)]);
            }
        }
    }

    void Spawn(ChickenBody.Kind kind)
    {
        if (FindObjectsOfType<ChickenAI>().Length >= 12) return; // 全域上限（含孵出來的小雞）

        for (int attempt = 0; attempt < 20; attempt++)
        {
            Vector3 pos = new Vector3(
                Random.Range(-areaHalf.x, areaHalf.x), 0f,
                Random.Range(-areaHalf.y, areaHalf.y));

            if (worm != null && Vector3.Distance(pos, worm.position) < minDistFromWorm) continue;
            if (!NavMesh.SamplePosition(pos, out NavMeshHit hit, 2f, NavMesh.AllAreas)) continue;

            chickens.Add(ChickenFactory.Create(hit.position, kind));
            return;
        }
    }
}
