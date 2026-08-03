using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

public enum GameState { Playing, GameOver }

/// 遊戲流程：計分（含連吃倍率）、險過獎勵、死亡、重開
public class GameManager : MonoBehaviour
{
    public static GameManager I { get; private set; }

    public GameState State { get; private set; } = GameState.Playing;
    public float Score { get; private set; }
    public float SurvivalTime { get; private set; }
    public int FoodEaten { get; private set; }

    // 連吃倍率
    public int ComboStreak { get; private set; }
    public float ComboTimeLeft { get; private set; }
    public float ComboMultiplier => Mathf.Min(1f + 0.5f * Mathf.Max(0, ComboStreak - 1), 5f);

    // 險過提示（HUD 淡出用）
    public float CloseCallFlash { get; private set; }
    public int CloseCalls { get; private set; }

    void Awake()
    {
        I = this;
        Time.timeScale = 1f;

        int ug = LayerMask.NameToLayer("Underground");
        int ck = LayerMask.NameToLayer("Chicken");
        if (ug >= 0 && ck >= 0) Physics.IgnoreLayerCollision(ug, ck, true);
    }

    void Update()
    {
        if (State == GameState.Playing)
        {
            SurvivalTime += Time.deltaTime;
            Score += 10f * Time.deltaTime;

            if (ComboTimeLeft > 0f)
            {
                ComboTimeLeft -= Time.deltaTime;
                if (ComboTimeLeft <= 0f) ComboStreak = 0;
            }
        }

        CloseCallFlash = Mathf.Max(0f, CloseCallFlash - Time.unscaledDeltaTime);

        var kb = Keyboard.current;
        if (State == GameState.GameOver && kb != null && kb.rKey.wasPressedThisFrame)
        {
            Time.timeScale = 1f;
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }
    }

    public void AddFoodScore(int points)
    {
        if (State != GameState.Playing) return;
        ComboStreak++;
        ComboTimeLeft = 10f;
        Score += points * ComboMultiplier;
        FoodEaten++;
    }

    /// 啄下來前一瞬間鑽土閃掉：+300、慢動作、震動
    public void CloseCall()
    {
        if (State != GameState.Playing) return;
        Score += 300f;
        CloseCalls++;
        CloseCallFlash = 1.6f;
        SynthSfx.Play("closecall", 0.7f);
        if (CameraFollow.Main != null) CameraFollow.Main.Shake(0.18f);
        StartCoroutine(SlowMo(0.45f, 0.5f));
    }

    IEnumerator SlowMo(float scale, float realSeconds)
    {
        Time.timeScale = scale;
        yield return new WaitForSecondsRealtime(realSeconds);
        if (State == GameState.Playing) Time.timeScale = 1f;
    }

    public void GameOver()
    {
        if (State == GameState.GameOver) return;
        State = GameState.GameOver;
        Time.timeScale = 0.3f;

        if (CameraFollow.Main != null) CameraFollow.Main.Shake(0.45f);

        // 蚯蚓被啄飛
        var worm = GameObject.FindGameObjectWithTag("Player");
        if (worm != null)
        {
            var rb = worm.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.freezeRotation = false;
                rb.useGravity = true;
                rb.AddForce(Vector3.up * 5f + Random.insideUnitSphere * 2f, ForceMode.Impulse);
                rb.AddTorque(Random.insideUnitSphere * 8f, ForceMode.Impulse);
            }
            ParticleFx.Burst(worm.transform.position + Vector3.up * 0.3f,
                new Color(1f, 0.6f, 0.7f), 24, 3.5f, 0.15f, 1.2f);
        }
    }

    void OnDestroy()
    {
        if (I == this) I = null;
    }
}
