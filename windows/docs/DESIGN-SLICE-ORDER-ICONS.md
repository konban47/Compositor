# 切片排列图标

2026-10-17，按用户要求使用 **内置 imagegen** 生成四张透明 PNG。参考用户提供的叠层与方向箭头示意；用户截图未收入仓库。未调用 CLI 或 API 回退。

1.5.14.0 起运行时改用统一图标体系中的矢量排列图标（`IconSet.cs` 的 `slice-front`／`slice-forward`／`slice-backward`／`slice-back`），本页四张生成图移到本目录保存，作为历史设计参考；文件未做图像编辑，SHA-256 不变。下表链接指向 1.5.3.1 时的原位置。

| 操作 | 资源 |
| --- | --- |
| 置于顶层 | [front.png](https://github.com/konban47/Compositor/blob/windows-v1.5.3.1/windows/src/Compositor.Desktop/Assets/slice-order/front.png) |
| 前移一层 | [forward.png](https://github.com/konban47/Compositor/blob/windows-v1.5.3.1/windows/src/Compositor.Desktop/Assets/slice-order/forward.png) |
| 后移一层 | [backward.png](https://github.com/konban47/Compositor/blob/windows-v1.5.3.1/windows/src/Compositor.Desktop/Assets/slice-order/backward.png) |
| 置于底层 | [back.png](https://github.com/konban47/Compositor/blob/windows-v1.5.3.1/windows/src/Compositor.Desktop/Assets/slice-order/back.png) |

## 生成提示词

### front

Use case: logo-brand. Asset type: one small monochrome toolbar icon for the Windows Compositor photo editor. Primary request: design the BRING SLICE TO FRONT icon, referencing the attached Photoshop stack-and-arrow layout only. Depict three compact stacked diamond-shaped sheets at right; the TOP sheet is solid white and lower sheets are thin outlined white chevrons. A slim upward arrow on the left reaches the very top, with a short stop bar above it, clearly meaning move to front. Flat precise geometric vector-like strokes, moderately rounded joins, same restrained weight as a 20 px desktop toolbar symbol. White ink only, fully transparent background and negative spaces. Single centered icon fills 78% of square canvas. No button box, no shadows, no gradient, no letters, no numbers, no extra icons. It must remain legible at 20 by 20 display pixels.

### forward

Use case: logo-brand. Asset type: a single small monochrome Windows Compositor toolbar icon, BRING SLICE FORWARD. Subject: three outlined stacked diamond sheets, the middle sheet solid, a short upward arrow on the left from the middle level to the upper level; no stop bar. Photoshop style stacked sheets and arrow at left, flat clean precise geometric vector-like graphic, pure SOLID BLACK shapes with medium-weight outlines, crisp antialiasing, no grain or texture or glow or grunge. Transparent background and transparent negative spaces. Single centered compact icon occupies 80 percent of square canvas, restrained slightly rounded joins, harmonious visual weight for 20 px UI glyph. No text, labels, numbers, button background, shadow or gradients. The ink will be tinted to the app theme by the UI.

### backward

Use case: logo-brand. Asset type: a single small monochrome Windows Compositor toolbar icon, SEND SLICE BACKWARD. Subject: three outlined stacked diamond sheets, the middle sheet solid, a short downward arrow on the left from the upper level to the middle level; no stop bar. Photoshop style stacked sheets and arrow at left, flat clean precise geometric vector-like graphic, pure SOLID BLACK shapes with medium-weight outlines, crisp antialiasing, no grain or texture or glow or grunge. Transparent background and transparent negative spaces. Single centered compact icon occupies 80 percent of square canvas, restrained slightly rounded joins, harmonious visual weight for 20 px UI glyph. No text, labels, numbers, button background, shadow or gradients. The ink will be tinted to the app theme by the UI.

### back

Use case: logo-brand. Asset type: a single small monochrome Windows Compositor toolbar icon, SEND SLICE TO BACK. Subject: three compact stacked diamond sheets at right, the bottom sheet solid and upper sheets outlined, a downward arrow on the left reaches the bottom, a short stop bar below it. Photoshop style stacked sheets and arrow at left, flat clean precise geometric vector-like graphic, pure SOLID BLACK shapes with medium-weight outlines, crisp antialiasing, no grain or texture or glow or grunge. Transparent background and transparent negative spaces. Single centered compact icon occupies 80 percent of square canvas, restrained slightly rounded joins, harmonious visual weight for 20 px UI glyph. No text, labels, numbers, button background, shadow or gradients. The ink will be tinted to the app theme by the UI.


## SHA-256

- front.png: `0f3ce9ed5de430ff0d6314a9e408f2f030bdd5924bdb3b81c8327f96ec13bd66`
- forward.png: `380bdc7c3005f6158f953c9b9520ec688504db54a292d12963ce3f83caddc714`
- backward.png: `9f803782408babd0e5fcb5ea1ffb18555f86db28da749a432abe9968ba2824a9`
- back.png: `64f1d63bbc279ee1a6581bcf9799ff6fa2713f2564f0eecd63d81bde78132e88`
