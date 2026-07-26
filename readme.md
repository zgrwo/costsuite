# BomAddIn (costsuite)

> 浼佷笟绾?BOM 绠＄悊涓庢垚鏈樊寮傚垎鏋?Excel 鎻掍欢锛欵xcel-DNA + C#锛岀洰鏍?10 涓囩骇鐗╂枡 / 100 涓囩骇鑺傜偣锛岀绾夸紭鍏堟灦鏋勩€?
---

## 瀹夎

1. 浠?[Releases](https://github.com/zgrwo/costsuite/releases) 涓嬭浇 `BomAddIn-packed.xll`
2. Excel 鈫?鏂囦欢 鈫?閫夐」 鈫?鍔犺浇椤?鈫?杞埌 鈫?娴忚 鈫?閫夋嫨 .xll
3. 鏈湴鏁版嵁搴撹嚜鍔ㄥ垵濮嬪寲锛圫QLite锛夛紝鏃犻渶棰濆閰嶇疆

### 楠岃瘉瀹夎

鍦ㄤ换鎰忓崟鍏冩牸杈撳叆锛?```
=BOM.PARSE("PN001", 1)
鈫?灞曞紑 BOM 鏍?```

---

## 妯″潡閫熻

> 瀹屾暣绛惧悕銆佸弬鏁拌鏄庤 **[API 鍙傝€僝(rules/api-reference.md)**锛涙瘡涓嚱鏁扮殑璇︾粏绀轰緥瑙?**[鐢ㄦ埛鎵嬪唽](rules/user-manual.md)**銆?
| 妯″潡 | 鍋氫粈涔?| 璇曚竴璇?|
|------|------|-------|
| `BOM` | 鐗╂枡娓呭崟绠＄悊锛堣В鏋?灞曞紑/宸紓锛?| `=BOM.VARIANCE(bom1, bom2)` |
| `Sync` | ERP 鏁版嵁鍚屾锛圫AP/Oracle/Excel锛?| 涓€閿悓姝ュ伐浣滅翱宸紓 |
| `Dashboard` | WPF 浠〃鐩橈紙瓒嬪娍/缁熻/鍛婅锛?| 鑷畾涔夐潰鏉胯鍥?|

---

## 浣跨敤妯″紡

### 宸ヤ綔琛?UDF

```vb
' BOM 瑙ｆ瀽锛堝崟灞傚睍寮€锛?=BOM.PARSE("PN001", 1)

' BOM 宸紓鍒嗘瀽锛堜袱涓増鏈姣旓級
=BOM.VARIANCE(bom_version1, bom_version2)

' 鏌ユ壘鐗╂枡璺緞锛堟墍鏈変娇鐢ㄤ綅缃級
=BOM.WHERESUSED("PN005")
```

### VBA 鑷姩鍖?
```vba
' 鍚庡彴鍚屾 ERP 鏁版嵁锛屼笉闃诲 Excel
Application.Run("SYNC.IMPORT", "MaterialMaster")
' 鍙缃畾鏃跺悓姝ラ棿闅?```

### 浠〃鐩?
鐐瑰嚮 Ribbon 鎸夐挳鎵撳紑 WPF 浠〃鐩橈紝瀹炴椂鏌ョ湅锛?- BOM 鐗堟湰宸紓瓒嬪娍
- 鎴愭湰鍙樺寲鐑姏鍥?- 鐗╂枡缂哄け鍛婅

---

## 鏋舵瀯鐗圭偣

```
UI 灞?(Ribbon + TaskPane + WPF)
  鈫?ExcelThreadDispatcher锛堣法绾跨▼ COM 璋冪敤锛?Service 灞?(BomService + SyncService)
  鈫?涓氬姟缂栨帓锛屾湁鐘舵€?浜嬪姟
Engine 灞?(VarianceCalculator)
  鈫?绾绠楋紝闆朵緷璧栵紝鍙嫭绔嬪崟鍏冩祴璇?Data 灞?(SQLite + DuckDB)
  绂荤嚎浼樺厛锛氭湰鍦扮紦瀛?+ 鎭㈠鍚庤嚜鍔ㄥ悓姝?```

- **SQLite**锛欳RUD 鎿嶄綔锛堢墿鏂欎富鏁版嵁銆丅OM 鐗堟湰锛?- **DuckDB**锛氬垎鏋愭煡璇紙宸紓鑱氬悎銆佽矾寰勫睍寮€锛?- **BFS 灞曞紑**锛氭浛浠ｉ€掑綊 CTE锛岄槻姝㈣矾寰勭垎鐐?- **绂荤嚎浼樺厛**锛氱綉缁滄柇寮€鑷姩鍒囨崲鏈湴锛屾仮澶嶅悗鑷姩鍚屾

---

## 閿欒澶勭悊

| 鍦烘櫙 | 琛屼负 |
|------|------|
| BOM 鑺傜偣涓嶅瓨鍦?| `#VALUE!` |
| 鐗╂枡缂栧彿鏍煎紡闈炴硶 | `#VALUE!` |
| 鏁版嵁搴撹繛鎺ュけ璐?| 鑷姩闄嶇骇 SQLite 鏈湴缂撳瓨 |
| ERP 鍚屾澶辫触 | Polly 閲嶈瘯 3 娆?+ Excel 瀵煎叆澶囩敤閫氶亾 |
| 绾跨▼ COM 鍐茬獊 | QueueAsMacro 鍥炶皟 UI 绾跨▼ |

---

## 瀹夊叏

- **BCrypt 璁よ瘉**锛氱敤鎴风櫥褰曞瘑鐮佸畨鍏ㄥ搱甯?- **DPAPI 鍔犲瘑**锛歐indows 鏁版嵁淇濇姢 API 鍔犲瘑鏁忔劅瀛楁
- **AES-256**锛氭湰鍦版暟鎹簱瀛楁绾у姞瀵?- **瀹¤鏃ュ織**锛欰OP 鎷︽埅鍏ㄩ噺鎿嶄綔瀹¤
- **ERP 閲嶈瘯**锛歅olly 鏂矾鍣ㄩ槻姝㈠悓姝ラ鏆?
---

## 璐ㄩ噺淇濊瘉

- **xUnit + Moq**锛氬崟鍏冩祴璇?+ 妯℃嫙娴嬭瘯
- **BenchmarkDotNet**锛氭€ц兘鍩哄噯娴嬭瘯锛圔OM 灞曞紑/宸紓璁＄畻锛?- **鍙屽紩鎿庢祴璇?*锛歋QLite 鍜?DuckDB 璺緞鐙珛楠岃瘉
- **绾跨▼瀹夊叏娴嬭瘯**锛氬帇鍔涙祴璇?30min锛? 绾跨▼寮傚父

---

## 宸茬煡闄愬埗

- **Windows Only**锛氫緷璧?Excel-DNA + COM + DPAPI
- **Excel 2016+**锛歯et472 鍩哄噯锛屼笉鏀寔 Excel 2013 鍙婁互涓?- **棣栨鍚屾**锛氬ぇ鍨?ERP 鍏ㄩ噺鍚屾鍙兘鑰楁椂鏁板垎閽燂紙鍚庣画澧為噺绉掔骇锛?
---

## 璐＄尞

璇烽槄璇?[CONTRIBUTING.md](CONTRIBUTING.md) 浜嗚В璐＄尞娴佺▼锛坒ork 鈫?PR 鈫?review锛夈€?
---

## 璁稿彲璇?
[MIT](LICENSE) 漏 zgrwo

---

## 浠庢簮鐮佹瀯寤?
```bash
# 寮€鍙戞瀯寤?dotnet restore && dotnet build && dotnet test

# 鍒嗗彂鏋勫缓锛堢敓鎴?.xll锛?dotnet build -c Release
ExcelDnaPack BomAddIn.dna
```

---

## 鏂囨。绱㈠紩

| 鏂囨。 | 瑙掕壊 | 鍐呭 |
|------|------|------|
| [API 鍙傝€僝(rules/api-reference.md) | 鏁板瓧鍞竴淇℃簮 | 8 涓?UDF 绛惧悕銆佸弬鏁拌鏄?|
| [鐢ㄦ埛鎵嬪唽](rules/user-manual.md) | 瀛︿範鏁欑▼ | 姣忎釜鍑芥暟璇︾粏绀轰緥 + 缁撴灉瑙ｈ |
| [context.md](rules/context.md) | 鏈琛?| 鎵€鏈夐鍩熸湳璇敮涓€瀹氫箟 |
| [project-structure.md](rules/project-structure.md) | 缁撴瀯鍦板浘 | 鏂囦欢鑱岃矗涓庡眰绾у叧绯?|
| [agents.md](agents.md) | 椤圭洰瀹硶 | 鏋舵瀯鍒嗗眰銆佺孩绾胯鍒欍€佸紑鍙戞祦绋?|
