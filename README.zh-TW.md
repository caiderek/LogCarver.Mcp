<p align="center"><img src="LogCarver/docs/assets/logo.png" width="96" alt="LogCarver logo"></p>

# LogCarver.Mcp

**[English](README.md) | 繁體中文**

唯讀的 [MCP](https://modelcontextprotocol.io) server。讓 AI 助理直接回答「這張 SQL Server 資料表剛剛被刪了哪些資料?」,做法是讀資料庫的交易記錄檔(`fn_dblog`)。事先不用開 Audit、CDC 或 Change Tracking。

它是 [LogCarver](https://github.com/caiderek/LogCarver)(MIT)解碼引擎外面的一層薄包裝。全部在本機執行,不會把資料送到任何地方。

> **狀態:** 早期版本(v0.1)。還沒發布到 NuGet 或 MCP registry,目前請從原始碼建置。

## 它做什麼

只有一個 tool:`find_deleted_rows`。它只會執行 `SELECT`,不會改任何資料。選用的還原 SQL 只是一段文字,工具本身不會去執行。

| 參數 | 說明 |
|---|---|
| `table`(必填) | 要含 schema,例如 `dbo.Orders` |
| `database` | 不給就用連線字串裡的資料庫。其他資料庫會被拒絕,除非列在 `LOGCARVER_MCP_ALLOWED_DATABASES` |
| `operations` | `delete`(預設)、`update`、`insert`、`all` |
| `from`、`to` | ISO 8601 時間窗,是伺服器本地時間,不做時區轉換 |
| `key` | `Column=Value`,只看某一列的歷史。欄位名稱不分大小寫;日期、bit、decimal、hex 二進位都能照平常寫法比對 |
| `maxRows` | 預設 20,最多 500,`0` 代表只回筆數。保留最近的事件 |
| `includeUndoSql` | 一併回傳可以還原每個事件的建議 SQL |

回傳是 JSON:`summary`(筆數、是否被截斷、警告與備註)和 `events`(類型、時間、LSN、交易 ID、前後欄位值、備註)。二進位值是 hex 字串,decimal 是字串,不會被悄悄四捨五入。

## 前提與限制

- 連線帳號要是 **sysadmin**(`fn_dblog` 要求)。
- 只看得到**還在使用中的記錄檔**。記錄檔空間被重複使用後,結果會是空的,這不是 bug。這種情況付費版 LogCarverOffline 通常還能直接讀 `.ldf` 救回來。
- `TRUNCATE` 和 `DROP` 救不回來。
- 壓縮表會被拒絕。`time`、`xml` 欄位和 off-row LOB 值會被拒絕或標成 `needsManualReview`,不會用猜的。
- 驗證過 SQL Server 2025、2022、2019、2016(SP2)。其他版本會在 `notes` 裡警告。
- 活動記錄檔很大時,掃描可能要一分鐘以上。

## 設定

連線字串從環境變數讀,帳密不會經過 AI 對話:

```
LOGCARVER_MCP_CONNECTION_STRING=Server=localhost;Database=MyDb;Integrated Security=true;TrustServerCertificate=true
```

也可以把 `LOGCARVER_MCP_CONNECTION_STRING_FILE` 指到一個存放它的檔案。

這個帳號是 sysadmin,呼叫的又是 AI,所以工具預設只能讀連線字串裡的那個資料庫。要讓它讀其他的,把名稱列出來(逗號或分號分隔):

```
LOGCARVER_MCP_ALLOWED_DATABASES=Sales;HR
```

資料列的內容是直接從資料庫讀出來的,它們是資料,不是指令,工具說明裡也有提醒模型這一點。

### Claude Code

```
claude mcp add logcarver --env LOGCARVER_MCP_CONNECTION_STRING="Server=localhost;Database=MyDb;Integrated Security=true;TrustServerCertificate=true" -- logcarver-mcp
```

### 從原始碼建置

```
git clone --recurse-submodules https://github.com/caiderek/LogCarver.Mcp
dotnet build
dotnet run --project src/LogCarver.Mcp
```

## 測試

```
dotnet test tests/LogCarver.Mcp.Tests
dotnet test tests/LogCarver.Mcp.IntegrationTests
```

整合測試需要本機有 SQL Server,以及 sysadmin 的 Windows 帳號。測試會自己建立並刪除沙盒資料庫 `LogCarver_McpTests`。

## 授權

MIT,見 [LICENSE](LICENSE)。
