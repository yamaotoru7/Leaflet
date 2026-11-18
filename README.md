# Leaflet

フォルダーのツリーと画像を 1 つのウィンドウに並べて読む、Windows 用のマンガ・画像ビューアーです。
ZIP / RAR / 7z の書庫を展開せずに開き、見開きで表示します。

けんじ氏の画像ビューアー **Leeyes** を参考に、いまの Windows（10 / 11、64 ビット）向けに作り直したものです。
Leeyes の画面の配置と読み方をそのまま続けられるようにしながら、日本語や絵文字のファイル名、壊れたファイルでも落ちないことを大事にしています。

[English](#english)

## できること

- **3 つのペイン**：左にフォルダーのツリーとファイル一覧、右に画像を並べます。ツリーには書庫と書庫の中のフォルダーも出るので、選ぶだけで読み始められます。
- **書庫を展開せずに読む**：ZIP / RAR / 7z（.cbz / .cbr / .cb7 も）をそのまま開きます。書庫の中のファイル名は、UTF-8 でも Shift_JIS でも読めます。
- **見開き**：単ページ・見開き・自動判定（横長の画像は 1 枚で表示）を切り替えられます。右綴じ・左綴じ、表紙を単ページにする、2 枚の高さを揃える、にも対応しています。
- **大きさ**：ウィンドウに合わせる・幅に合わせる・高さに合わせる・原寸と、拡大はしない。補間の質も選べます。
- **続きから読む**：書庫ごとに最後に読んだページを覚え、次に開くとそこから表示します。最後のページの次は、同じフォルダーの次の書庫へ進みます。
- **ブックマークと履歴**：ページにブックマークを付け、メニューから開けます。最近開いた書庫もメニューから開けます。
- **全画面**：画像だけを表示します。メニューはマウスを動かすとしばらく出ます。
- **画像の形式**：JPEG / PNG / GIF / BMP / TIFF / WebP / AVIF（Windows に入っているコーデックで表示します）。WebP と AVIF は Microsoft Store の「WebP 画像拡張機能」「HEIF 画像拡張機能」「AV1 ビデオ拡張機能」が要ります。
- 壊れた画像や読めない形式は、そのページに理由を表示し、前後のページへは普通に進めます。

## インストール

1. [Releases](https://github.com/yamaotoru7/Leaflet/releases) から `Leaflet-<版>-setup.exe` をダウンロードして実行します。
2. 管理者権限は要りません。`%LocalAppData%\Programs\Leaflet` に入ります。.NET は同梱しているので、ほかにインストールするものはありません。

インストーラーにはコード署名をしていないため、初回は「Windows によって PC が保護されました」と表示されることがあります。その場合は「詳細情報」→「実行」で進めてください。

設定と読んだ位置は `%LocalAppData%\Leaflet\settings.json` に保存します。上書きインストールやアンインストールでは消えません。

### 更新

起動したとき（1 日に 1 回まで）、GitHub に新しい版が出ていないかを確かめます。新しい版があればメニューバーに「<版> に更新」が出て、押すと変更内容を見てから更新できます。**ヘルプ → 更新を確認** でいつでも確かめられます。
ダウンロードしたインストーラーは、リリースに添付した SHA-256 と照合してから実行します。

## 使い方

ツリーで書庫かフォルダーを選ぶか、ファイルをウィンドウにドロップするか、アドレスバーにパスを入れて Enter を押すと開きます。

| 操作 | 動き（右綴じのとき） |
| --- | --- |
| ホイール、↓ / ↑、PageDown / PageUp | 次 / 前の見開き |
| ← / → | 1 ページ進む / 戻る（見開きのずれを直す。左綴じでは逆） |
| Home / End | 最初 / 最後のページ |
| Ctrl+↓ / Ctrl+↑ | 次 / 前の書庫 |
| Enter、F11 | 全画面の切り替え |
| Esc | 全画面を抜ける。全画面でなければ画像を閉じる（書庫は開いたまま） |
| Ctrl+O | ファイル・書庫を開く |
| Ctrl+D | ブックマークを付ける |
| 右クリック | 見開きや大きさのメニュー |

## ソースからビルドする

必要なもの：Windows、[.NET 10 SDK](https://dotnet.microsoft.com/download)

```sh
dotnet build                               # ビルド
dotnet test                                # テスト
dotnet run --project src/Leaflet.App       # 起動
```

インストールして使っている設定を書き換えずに試すには、環境変数 `LEAFLET_SETTINGS` で設定ファイルを別の場所にします（例：`set LEAFLET_SETTINGS=%TEMP%\leaflet-dev.json`）。

インストーラーを作るには、[Inno Setup 6](https://jrsoftware.org/isinfo.php) を入れてから次を実行します。出力は `.work/installer/` です。

```sh
winget install --id JRSoftware.InnoSetup -e --scope user
pwsh tools/installer/build-installer.ps1
```

## 謝辞

Leaflet は、けんじ氏が作られた画像ビューアー **Leeyes** を参考にして作りました。

Leeyes は、エクスプローラーのようなツリーとファイル一覧に画像の窓を並べた 3 ペインの画面で、フォルダーや書庫にまとめた画像を次々に読める、わかりやすく使いやすいビューアーです。
画面の配置、見開きの組み方、キーとマウスの操作、最後のページから次の書庫へ進む動きなど、Leaflet の読み方のほとんどは Leeyes の設計にならっています。
長く使い続けられるすぐれたソフトウェアを公開してくださったけんじ氏に、心から感謝します。

- Leaflet のコードと画像は新しく書いたもので、Leeyes のプログラムやデータは含んでいません。
- Leaflet は Leeyes の公式版や後継版ではなく、けんじ氏とは関係のない個人のプロジェクトです。Leaflet についてのお問い合わせを、けんじ氏に送らないようお願いします。

## ライセンス

[MIT License](LICENSE) です。

書庫の読み込みに [SharpCompress](https://github.com/adamhathcock/sharpcompress)（MIT License）を使っています。同梱している第三者のソフトウェアのライセンスは [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt) にあります。

---

## English

Leaflet is a manga and image viewer for Windows 10 / 11 (64-bit). It shows a folder tree, a file list and the pages side by side in one window, and reads ZIP / RAR / 7z archives without extracting them.
It offers single-page, two-page spread and automatic layouts, right-to-left and left-to-right binding, resumes each book where you left off, and moves on to the next archive after the last page.

It is a re-creation for modern Windows of **Leeyes**, an image viewer by Kenji.

- **Install**: download `Leaflet-<version>-setup.exe` from [Releases](https://github.com/yamaotoru7/Leaflet/releases). No administrator rights or separate .NET installation are needed. The installer is not code-signed, so Windows SmartScreen may warn you on first run.
- **Updates**: the installed app checks GitHub for a new release at most once a day, and on **Help → 更新を確認** (Check for updates). The installer is verified against the published SHA-256 before it runs.
- **Build**: `dotnet build` with the .NET 10 SDK. See the Japanese section above for the installer.

The user interface is in Japanese.

### Acknowledgements

Leaflet is inspired by, and closely follows the design of, **Leeyes** by Kenji.
Its three-pane layout, the way it lays out two-page spreads, its keyboard and mouse controls and moving on to the next archive all come from Leeyes.
Many thanks to Kenji for creating such a thoughtful viewer and making it available to everyone.

Leaflet contains no code or data from Leeyes. It is an independent project, not an official version or successor of Leeyes, and is not affiliated with Kenji, so please do not contact Kenji about it.

### License

[MIT License](LICENSE). Archives are read with [SharpCompress](https://github.com/adamhathcock/sharpcompress) (MIT License); see [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt) for the third-party licenses included.
