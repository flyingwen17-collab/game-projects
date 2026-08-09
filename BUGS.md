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

- **[SumoParty][手感] 招式強度需要實玩調校 —— 目前數值是「機制成立」不是「好玩」**
  自測量到的：突き 推 0.574 m、突進破防 0.271 m、寄り 0.747 m、
  **いなし 讓突進方衝過頭 3.95 m**（土俵直徑才 4.55 m，太誇張）。
  另外突進（大招）推的距離目前小於突き（小招），直覺上不對。
  對策：`SumoConfig` 裡調 `whiffStumble`、`chargeForce`、`thrustForce`。
  但**只有實玩才知道對不對**，自測只能保證機制有效。
  記錄日期：2026-08-09

## 已修

(目前無)
