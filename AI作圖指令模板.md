# AI 作圖 → 遊戲素材 指令模板

> 版本：v1.0（2026-08-03）
> 用途：用 ChatGPT（或任何 AI 繪圖工具）產出可直接進 Unity 的素材
> 存放位置：圖片請放到 `E:\下載\CLAUDE\ｇａｍｅ\_Refs\` 對應資料夾，然後告訴我檔名，我就能直接看

---

## 0. 先搞清楚：AI 圖能做什麼、不能做什麼

| 用途 | 可用性 | 說明 |
|------|--------|------|
| **風格參考 / 概念圖** | ★★★★★ | **最高價值**。我看圖 → 直接翻譯成 Unity 的顏色、光照、後處理參數 |
| **粒子貼圖**（煙、火花、灰塵、光暈） | ★★★★★ | 本質就是一張灰階圖，AI 做得很好，成功率最高 |
| **UI 圖示 / 介面元素** | ★★★★☆ | 好用，但圖上的文字會亂碼，文字要用 TextMeshPro 另外疊 |
| **宣傳圖 / Logo / 商店封面** | ★★★★☆ | 很適合，反正只是一張圖 |
| **Decal 貼花**（塗鴉、水漬、裂痕） | ★★★★☆ | 好用，要去背 |
| **材質貼圖**（柏油、草地、木頭） | ★★☆☆☆ | ⚠️ 陷阱多，見第 4 節。**建議直接用 ambientCG 的 CC0 材質，品質好十倍** |
| **天空盒** | ★★☆☆☆ | ⚠️ 需要 equirectangular 全景，AI 接縫會對不上。**建議用 Poly Haven 的免費 HDRI** |
| **3D 模型** | ❌ | 做不到。AI 圖是平面的 |
| **角色多角度立繪** | ❌ | 一致性太差，同一個角色每張都不一樣 |

**一句話結論**：把 AI 作圖用在「**概念圖 + 粒子 + UI + 宣傳圖**」這四項，剩下的用 CC0 資源庫。

---

## 1. 所有指令都要加的「通用約束」

AI 預設會產出「漂亮的插畫」，但遊戲素材要的是「乾淨的素材」。以下這些字幾乎每個指令都要加：

| 你要的 | 加這句英文 | 為什麼 |
|--------|-----------|--------|
| 沒有光影烘焙 | `flat even lighting, no shadows, no highlights, no ambient occlusion` | 貼圖上如果已經有陰影，進 Unity 再打光會「雙重打光」，看起來很髒 |
| 沒有透視 | `orthographic top-down view, no perspective` | 材質貼圖必須是正投影 |
| 乾淨背景 | `isolated on pure black background` (粒子) / `on plain white background` (圖示) | 方便去背 |
| 正方形 | `1:1 square aspect ratio` | 遊戲貼圖要 2 的次方尺寸（512/1024/2048） |
| 沒有多餘裝飾 | `no text, no watermark, no border, no frame, no signature` | AI 很愛加簽名跟邊框 |
| 沒有暗角 | `no vignette, no depth of field, no blur` | 暗角會讓平鋪材質露餡 |

> **建議用英文下指令**，圖像模型對英文的理解精準很多。中文說明我在下面每一則都附上了。

---

## 2. 風格參考圖（★ 最重要，優先做這個）

**用途**：讓我看到你心中的「精美」長什麼樣。我看完可以直接產出對應的 Unity 材質、色票、燈光角度、後處理數值。

### 2-1 甩尾遊戲（DriftGame）

```
A third-person game screenshot of a stylized low-poly rally car drifting on a
mountain road at golden hour. Cel-shaded flat colors, strong silhouettes,
minimal texture detail, warm orange sunlight with long shadows, distant hazy
mountains, dust particles behind the car. Clean readable composition.
Video game screenshot, 16:9 widescreen. No text, no UI, no watermark.
```
> 中文：第三人稱、低多邊形風格化拉力車、山路甩尾、黃昏、賽璐璐平面上色、強烈剪影、暖橘陽光長影、遠山薄霧、車後揚塵。

**變化版**（想看不同方向就換掉關鍵字）：
- 換時間：`golden hour` → `blue hour twilight` / `overcast grey afternoon` / `neon-lit night city`
- 換風格：`cel-shaded flat colors` → `soft painterly gouache style` / `minimalist flat vector, almost no texture` / `clean semi-realistic PBR`
- 換場景：`mountain road` → `coastal cliff highway` / `industrial harbor with shipping containers` / `snowy forest road`

### 2-2 蚯蚓遊戲（蚯蚓的一生）

```
A cute stylized 3D game screenshot, low camera angle close to the ground,
a cartoon earthworm crawling through grass in a farmyard, a large hen visible
in the background looking menacing. Bright saturated colors, soft rounded shapes,
children's book illustration feel, soft rim lighting, shallow depth of field
on background only. Video game screenshot, 16:9. No text, no UI, no watermark.
```
> 中文：可愛風格化 3D、貼近地面的低視角、卡通蚯蚓在農場草地爬行、背景有隻大母雞、明亮飽和、圓潤造型、繪本感、柔和輪廓光。

### 2-3 「一次比較多種風格」的懶人指令

```
A 2x2 comparison grid showing the SAME scene (a stylized rally car on a mountain
road) rendered in 4 different art styles:
top-left: minimalist flat low-poly, no textures;
top-right: cel-shaded anime with black outlines;
bottom-left: soft hand-painted stylized;
bottom-right: semi-realistic PBR.
Consistent camera angle and composition in all four. No text labels.
```
> 中文：同一個場景用 4 種風格畫成 2x2 對照，一次比較。**這張最有效率**，你看完直接告訴我選哪一格。

---

## 3. 粒子貼圖（★ 成功率最高，馬上就能用進遊戲）

**關鍵**：一律 **純黑背景 + 灰階 + 置中 + 邊緣柔和**。Unity 的粒子用 Additive 混合模式時黑色會自動變透明，我也可以寫腳本幫你把黑底轉成 Alpha 透明。

### 3-1 煙霧 / 揚塵（甩尾必備）
```
A single soft white smoke puff, centered, isolated on pure pure black background.
Grayscale only, no color. Soft feathered edges, wispy organic shape,
radially balanced. Flat, no lighting direction, no shadow.
1:1 square, high resolution. No text, no watermark, no border.
```

### 3-2 火花 / 碰撞火星
```
A burst of small bright white sparks radiating from the center, isolated on
pure black background. Grayscale, thin streak shapes of varying length,
sharp bright cores with soft glow falloff. 1:1 square. No text, no watermark.
```

### 3-3 光暈 / 光斑（車燈、發光物件）
```
A soft circular radial glow, pure white center fading smoothly to pure black
at the edges, perfectly centered and radially symmetrical, no rings, no lens
flare streaks, no color fringing. Grayscale. 1:1 square. No text.
```

### 3-4 落葉 / 碎片（可做 8 宮格一次產一組）
```
A 4x2 grid of 8 different cartoon leaf silhouettes, pure white shapes on pure
black background, each leaf centered in its own cell, flat solid fill,
no outlines, no gradients, no shadows, evenly spaced. 2:1 aspect ratio.
No text, no grid lines.
```
> 這種「一張圖切成多格」的做法叫 **Sprite Sheet / Atlas**，我可以寫 Unity Editor 腳本幫你自動切開。

---

## 4. 材質貼圖（⚠️ 陷阱最多，先看警告）

### 4-1 三個必踩的坑

1. **AI 幾乎做不出真正的無縫平鋪**。你把圖鋪到地面上，接縫會很明顯。
   → 補救：用 GIMP/Krita 的「偏移（Offset）」濾鏡把接縫移到中間再修補，或用免費軟體 **Materialize** 處理。
2. **AI 圖自帶光影**。上面已經有陰影和高光，進 Unity 再打光會變成「雙重打光」，看起來髒且假。
   → 補救：指令一定要加 `flat even lighting, no shadows`，但 AI 常常不聽話。
3. **只有顏色圖（Albedo），沒有法線 / 粗糙度 / 高度圖**。PBR 材質需要一整組。
   → 補救：**我可以寫一個 Unity Editor 腳本，從你的顏色圖用 Sobel 演算法自動生成法線貼圖（Normal Map）跟粗糙度圖。** 品質比不上專業製作，但風格化夠用。

> **我的建議**：材質這一項直接去 **ambientCG.com** 下載 CC0 的完整 PBR 材質組（顏色 + 法線 + 粗糙度 + 位移全都有），比 AI 生成快 10 倍、好 10 倍、還完全免費。AI 只在「找不到你要的特殊材質」時才用。

### 4-2 真的要用的話，指令這樣下

```
A seamless tileable texture of weathered grey asphalt road surface with fine
gravel and small cracks. Perfectly flat even lighting, completely uniform
brightness across the entire image, no shadows, no highlights, no vignette,
no lighting direction. Orthographic top-down view, zero perspective.
PBR albedo / base color map only. 1:1 square, high resolution.
No text, no watermark, no border.
```
> 中文：無縫平鋪的風化灰柏油路面材質、細碎石與裂縫、完全均勻的平光、無陰影無高光無暗角、正投影俯視、只要 Albedo 顏色圖。

**替換材質名稱即可**：
`weathered grey asphalt` → `dry cracked dirt soil` / `lush green grass, top-down` / `rough concrete` / `wooden planks` / `stylized hand-painted grass, cartoon style`

**風格化版本**（比寫實版容易成功很多，也更符合我們的風格路線）：
```
A seamless tileable hand-painted stylized cartoon grass texture, top-down
orthographic view, simplified brush-stroke shapes, limited palette of 3 green
tones, flat even lighting, no shadows, no gradients, no perspective.
Game texture, 1:1 square. No text, no watermark.
```

---

## 5. UI 圖示

```
A set of 9 flat vector game UI icons arranged in a clean 3x3 grid on a plain
white background. Icons: speedometer, trophy, gear/settings, circular restart
arrow, pause button, star, padlock, checkered flag, sound speaker.
Consistent thick rounded outline style, single flat color fill, uniform
line weight, uniform size, generous even spacing, centered in each cell.
No text, no labels, no shadows, no gradients, no background decoration.
```
> 中文：3x3 排列的 9 個扁平向量遊戲 UI 圖示、白底、統一的粗圓角外框風格、單色填色、線寬一致、無文字無陰影無漸層。

**重點**：
- **一次要一組**，這樣風格才會統一。分次產出的圖示風格一定對不上。
- **不要讓 AI 寫文字**，一定亂碼。介面文字用 Unity 的 TextMeshPro 疊上去。
- 產完給我，我寫腳本自動切成單張 + 設定好 Sprite 匯入參數。

---

## 6. 天空盒（能力有限，建議改用 HDRI）

AI 做不出正確的 **equirectangular 全景**（左右邊緣接不起來、天頂會扭曲），所以：

**建議做法**：去 **polyhaven.com/hdris** 下載免費 CC0 的 HDRI（真實拍攝的全景），直接拖進 Unity 當天空盒 + 環境光。**這是 M0 裡效果最好的一步。**

**AI 只適合做「純漸層卡通天空」**（沒有具體物件就沒有接縫問題）：
```
A smooth vertical gradient sky, deep indigo blue at the top transitioning
smoothly through soft lavender to warm peach orange at the bottom.
Completely smooth gradient, no clouds, no sun, no stars, no objects,
no texture, no noise, no banding. 2:1 horizontal aspect ratio.
```

---

## 7. 宣傳圖 / 商店封面 / Logo

```
A key art poster for an indie arcade drift racing game. A stylized low-poly
rally car sliding sideways with a dramatic dust trail, dynamic diagonal
composition, dramatic sunset backlighting with strong rim light, bold
saturated color palette of orange and teal, clean empty space in the upper
third reserved for a title. Video game key art, 16:9. No text, no logo,
no watermark.
```
> 中文：獨立街機甩尾遊戲的主視覺、低多邊形拉力車橫滑揚塵、動態對角構圖、夕陽逆光強輪廓光、橘藍撞色、上方三分之一留白給標題。
> **留白給標題**很重要，標題用真的字型疊上去，不要讓 AI 畫文字。

---

## 8. 你產完圖之後的流程

### 步驟
1. 圖片存到 `E:\下載\CLAUDE\ｇａｍｅ\_Refs\` 底下對應資料夾：
   ```
   _Refs\01_風格參考\    ← 概念圖、參考截圖
   _Refs\02_粒子貼圖\    ← 煙、火花、光暈
   _Refs\03_UI圖示\
   _Refs\04_材質貼圖\
   _Refs\05_天空盒\
   _Refs\06_宣傳圖\
   ```
2. 告訴我檔名（例如「我放了 `01_風格參考\drift_ref_01.png`」）
3. 我會直接開圖來看，然後做這些事 👇

### 我拿到圖之後能做的事

| 你給我 | 我產出 |
|--------|--------|
| **風格參考圖** | 分析出主色/輔色/強調色 → 寫成 Unity 色票 ScriptableObject；推算燈光角度與色溫、後處理數值（Bloom 強度、色調曲線、飽和度）→ 寫成 Editor 腳本，你按一下就套用 |
| **粒子貼圖** | 寫 Editor 腳本把黑底轉成透明 Alpha、設定匯入參數、建好 Particle System 的材質與參數 |
| **UI 圖示組** | 寫腳本自動切格、設定 Sprite、產生 UI Prefab |
| **材質圖** | 寫 Editor 腳本從顏色圖生成法線貼圖與粗糙度圖、建立 URP 材質、檢查平鋪接縫 |
| **任何圖** | 批次重新命名、轉 2 的次方尺寸、設定壓縮格式、產生匯入預設 |

---

## 9. 幾個實用提醒

1. **給我圖的時候順便說「哪一點吸引你」** —— 是顏色？光線？造型比例？氣氛？這比圖本身更有資訊量。你說「我喜歡那個橘色的光」跟你說「這張好看」，我做出來的東西差很多。

2. **同一批素材要一次產完**。AI 每次生成的風格都會漂移，分五次產的五個圖示放在一起會像五個不同遊戲的東西。

3. **解析度**：遊戲貼圖用 2 的次方（512 / 1024 / 2048）。AI 通常輸出 1024x1024，剛好可用。長方形的（16:9 概念圖）只是參考用，不進遊戲，不用管尺寸。

4. **版權**：OpenAI 目前的使用條款把生成圖的所有權給使用者，商用可以。但企劃書第 7 節提過的原則不變 —— **主要視覺資產別全靠 AI**，概念圖、粒子、宣傳圖這類用 AI 沒問題，核心角色與場景還是建議用 CC0 資源或自製。

5. **AI 圖不能取代 3D 模型**。畫得再漂亮的概念圖，進到遊戲裡還是需要有人把它做成 3D。AI 圖的價值是「讓我知道要做成什麼樣子」，不是「省掉建模」。

---

## 10. 如果你只想產一張圖，就產這張

**2x2 風格對照圖**（第 2-3 節那則指令）。

看完你直接告訴我選哪一格，整個專案的美術方向就定案了 —— 這是整份文件裡最有效率的一步。
