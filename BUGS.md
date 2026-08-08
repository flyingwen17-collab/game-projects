# BUGS — 未修 bug 清單

> 規則(流程 MD §5):發現當下記一筆「現象 + 怎麼觸發」;修完劃掉並註記日期。
> 有「會壞流程」級 bug 未修時,不開新功能。

## 未修

- **[SumoParty][效能] 每幀 GC 配置約 2000 bytes，峰值 231 KB —— 違反流程 MD §6.1「GC Alloc = 0」**
  量測方式：`Builds/dev_baseline/SumoParty.exe -perftest dev_baseline`（development build，
  12 秒取樣 4428 幀）。對照組與實驗組都是約 2000 bytes/frame，所以跟 URP 設定無關，
  是遊戲程式本身每幀在配置記憶體。
  影響：跑久了必然觸發 GC，造成週期性卡頓尖峰。目前場景輕（370+ FPS）不明顯，
  P3 量產後會浮現。
  下一步：用 Profiler 找每幀配置的來源（常見元凶：字串串接、`FindObjectsByType`、
  LINQ、每幀 new Vector3[] / List、Debug.Log）。
  記錄日期：2026-08-09

- **[SumoParty][物理] 力士自動轉向在 `Update` 直接設 `transform.rotation`，會跟物理求解器搶控制權**
  （`SumoWrestler.cs:131`）。現象：兩人貼身互推/被推飛時轉向可能抖動或吃掉碰撞反應。
  觸發：兩名力士貼緊互推數秒，或在被推飛（knockTimer 期間）觀察朝向。
  對策：改用 `rb.MoveRotation()` 在 `FixedUpdate` 做（流程 MD §6.4 鐵則 5）。
  記錄日期：2026-08-08（v3.0 流程審查時發現，尚未實玩確認嚴重度）

## 已修

(目前無)
