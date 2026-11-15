# VNPT-Steam-Bypass
Một giải pháp khắc phục có mục tiêu dành cho việc VNPT chặn Steam được ghi nhận trong tài liệu [`VNPT-Steam-Report.md`](docs/VNPT-Steam-Report.md), cùng với relay chống lọc SNI của mạng HCMUS được ghi nhận trong [`scripts\campus\README.md`](scripts/campus/README.md).

Một tệp EXE duy nhất — `dist\VNPT-Steam-Bypass.exe` — cung cấp cả hai chế độ, chọn ngay trong giao diện:
| Chế độ | Cách hoạt động | Dùng khi nào |
|---|---|---|
| **VNPT (split DNS)** | Quy tắc NRPT của Windows và một khối hosts nhỏ được quản lý | Steam bị chặn bởi DNS trên đường VNPT |
| **Campus (HCMUS relay)** | Relay TLS phân mảnh chạy trên localhost và PAC chỉ dành cho Steam | Steam bị reset TLS do lọc SNI trên Wi-Fi trường |

Chỉ một chế độ chạy tại một thời điểm; khi chuyển chế độ, chế độ đang bật sẽ được tắt trước. Relay chính là tệp EXE này khi chạy với tham số ẩn `--relay`, vì vậy không có tệp EXE thứ hai.

## Chế độ VNPT: công cụ này thay đổi những gì
| Lớp | Thay đổi | Phạm vi |
|---|---|---|
| DNS | Các quy tắc NRPT của Windows chỉ gửi các tên miền của Steam/Valve tới `8.8.8.8` / `8.8.4.4` | Chỉ các tên miền Steam |
| Hosts | Một khối nhỏ được quản lý sẽ ghim một vài tên miền mà Google vẫn phân giải tới máy chủ biên (edge) quốc tế bị lọc | Chỉ các tên miền Steam |
| Mọi thứ khác | Giữ nguyên | Tất cả các DNS khác và mọi lưu lượng mạng khác |

## Chế độ Campus (HCMUS relay)
- Chạy relay HTTP CONNECT chỉ trên localhost như một tiến trình con ẩn của cùng tệp EXE.
- Chia nhỏ các bản ghi TLS handshake theo chiều client→server thành các bản ghi 64 byte, vượt qua bộ lọc SNI mà không giải mã lưu lượng.
- Đặt PAC proxy của người dùng thành `http://127.0.0.1:<cổng>/steam.pac`; chỉ các tên Steam/Valve đi qua relay, mọi thứ khác vẫn `DIRECT`.
- Từ chối các tên không phải Steam/Valve bằng `403`, nên không thể bị lạm dụng làm proxy mở.
- Sao lưu cấu hình proxy trước đó và khôi phục khi bấm **Turn OFF**.

Công cụ **không** cài đặt VPN, proxy, trình điều khiển bộ lọc (filter driver), tuyến định tuyến (route), hay dịch vụ chạy ngầm nào. Nó không can thiệp vào bộ định tuyến (router), vì vậy các thiết bị khác trên cùng mạng Wi-Fi không bị ảnh hưởng.

## Tại sao cách này không làm giảm tốc độ Wi-Fi/Ethernet
- Không có đường hầm (tunnel) hay lớp phủ (overlay): các gói tin vẫn đi theo đường truyền trực tiếp của VNPT.
- Các truy vấn DNS không phải của Steam vẫn sử dụng trình phân giải hiện tại (`1.1.1.1` trong trường hợp này).
- Lưu lượng truy cập không phải của Steam không bị chuyển qua proxy, điều chỉnh băng thông (shaped), hay kiểm tra bởi công cụ này.
- Store và Community được phân giải tới các nút (node) Akamai **bên trong mạng VNPT**, ngắn hơn so với đường truyền đi qua Hồng Kông đang bị chặn. Cách khắc phục này sẽ giúp giảm độ trễ cho Steam chứ không làm tăng thêm.
- Chi phí phát sinh duy nhất là việc tra cứu DNS cho các tên miền Steam thông qua Google DNS. Việc này thường chỉ mất vài chục mili giây và chỉ áp dụng cho các tên miền của Steam; các tên miền Store/Community đã được ghim sẽ bỏ qua bước phân giải DNS hoàn toàn.

## Các tệp tin
| Tệp | Mục đích |
|---|---|
| `dist\VNPT-Steam-Bypass.exe` | Tệp EXE duy nhất: GUI và chế độ ẩn `--relay` |
| `Build-Exe.ps1` | Biên dịch tệp EXE kết hợp và nhúng các tập lệnh PowerShell của VNPT vào trong |
| `src\SteamBypassGui.cs` | Mã nguồn ứng dụng GUI và logic chuyển chế độ |
| `src\CampusMode.cs` | Vòng đời relay/PAC/proxy và phần xác minh chế độ Campus/HCMUS |
| `src\UiControls.cs` | Các điều khiển WinForms giao diện tối (dark-theme) |
| `src\app.manifest` | Yêu cầu quyền Quản trị viên (Administrator) cho tệp EXE |
| `src\SteamSnIRelay.cs` | Mã nguồn relay được biên dịch vào EXE dưới dạng `--relay` |
| `scripts\campus\Enable-CampusRelay.ps1` | Khởi động chế độ relay của EXE kết hợp và áp dụng PAC |
| `scripts\campus\Disable-CampusRelay.ps1` | Dừng tiến trình con relay của chế độ Campus và khôi phục cấu hình proxy trước đó |
| `scripts\campus\Test-CampusRelay.ps1` | Xác minh qua dòng lệnh cho chế độ Campus |
| `scripts\campus\CampusRelay.Common.ps1` | Các hàm hỗ trợ dòng lệnh: danh sách hậu tố, bộ ghi PAC, kiểm tra HTTP PAC/WinHTTP, sao lưu/khôi phục proxy |
| `src\CampusMode.cs` | Vòng đời relay/PAC/proxy phía GUI và phần xác minh chế độ Campus/HCMUS |
| `scripts\vnpt\Enable-VnptBypass.ps1` | Thêm các quy tắc NRPT và khối hosts được quản lý; sao lưu tệp `hosts` trước khi thực hiện |
| `scripts\vnpt\Disable-VnptBypass.ps1` | Xóa các quy tắc/khối đã thêm và khôi phục các dòng hosts liên quan đến Steam trước đó |
| `scripts\vnpt\Test-VnptBypass.ps1` | Xác minh DNS, NRPT, tệp hosts và các phản hồi HTTPS thực tế |
| `scripts\vnpt\VnptBypass.Common.ps1` | Các hàm hỗ trợ dùng chung và danh sách tên miền |
| `scripts\vnpt\Test-SteamVnpt.ps1` | Tập lệnh chẩn đoán cơ sở chỉ đọc (read-only) ban đầu từ báo cáo |

## Giao diện đồ họa - GUI (khuyên dùng)
`dist\VNPT-Steam-Bypass.exe` là tệp EXE Windows độc lập duy nhất. Nó nhúng sẵn bốn tập lệnh PowerShell của VNPT và relay HCMUS, vì vậy tệp EXE có thể được sao chép tới bất kỳ đâu trên máy tính này và tự chạy độc lập.
1. Nhấp đúp vào tệp EXE và chấp nhận hộp thoại UAC.
2. Trong hàng **MODE**, chọn **VNPT (split DNS)** hoặc **Campus (HCMUS relay)**. Khi chuyển chế độ, chế độ đang bật sẽ được tắt trước.
3. Thẻ trạng thái hiển thị **VNPT MODE IS ON/OFF/PARTIAL** hoặc **CAMPUS MODE IS ON/OFF/PARTIAL**.
4. Nhấp **Turn ON** / **Turn OFF** / **Repair** để thay đổi chế độ đã chọn.
5. Nhấp **Test connection** để kiểm tra chế độ đã chọn.
6. Nhấp **Open data folder** để xem `%ProgramData%\VNPT-Steam-Bypass`, nơi tệp EXE giải nén các tập lệnh VNPT và ghi nhật ký cùng bản sao lưu hosts. Nhật ký Campus và bản sao lưu proxy nằm trong `%LOCALAPPDATA%\HCMUS-Campus`.

GUI yêu cầu quyền quản trị viên vì chế độ VNPT thay đổi các quy tắc NRPT và tệp `hosts`. Chế độ Campus chỉ ghi cấu hình proxy của người dùng hiện tại và chạy relay trên loopback. Các tập lệnh sẽ được giải nén lại mỗi khi tệp EXE khởi động, do đó tệp EXE luôn sử dụng đúng các tập lệnh đã được nhúng tại thời điểm nó được biên dịch. Quá trình biên dịch chỉ tạo ra một tệp EXE duy nhất.

Để biên dịch lại sau khi thay đổi các tập lệnh hoặc mã nguồn GUI:
```powershell
.\Build-Exe.ps1
```
Yêu cầu: Windows 10/11 có sẵn .NET Framework 4.x (được tích hợp sẵn trong Windows). Quá trình biên dịch sử dụng trình biên dịch của .NET Framework (`csc.exe`); không yêu cầu cài đặt SDK.
Tệp EXE này không có chữ ký số (code-signed). Nếu Windows SmartScreen hiển thị cảnh báo, hãy chọn **More info** → **Run anyway**. Windows cũng sẽ hỏi quyền quản trị viên thông qua UAC vì công cụ này thay đổi DNS hệ thống và tệp `hosts`.

## Sử dụng qua dòng lệnh
Mở **PowerShell với quyền Quản trị viên (Run as Administrator)**, sau đó chạy:
```powershell
Set-Location 'C:\Users\MSI VN\Desktop\VNPT-Steam-Bypass'
# Tùy chọn: xem trước các thay đổi mà không ghi bất cứ thứ gì vào hệ thống
.\scripts\vnpt\Enable-VnptBypass.ps1 -DryRun
# Áp dụng
.\scripts\vnpt\Enable-VnptBypass.ps1
# Xác minh
.\scripts\vnpt\Test-VnptBypass.ps1
```
Chế độ Campus/HCMUS từ dòng lệnh (cũng trong PowerShell quyền Quản trị viên):
```powershell
Set-Location 'C:\Users\MSI VN\Desktop\VNPT-Steam-Bypass\scripts\campus'
.\Enable-CampusRelay.ps1
.\Test-CampusRelay.ps1
```
Thoát hoàn toàn Steam, bao gồm cả biểu tượng ở khay hệ thống (system tray), sau đó khởi động lại. Nếu trình duyệt vẫn hiển thị trang cũ, hãy chạy lệnh `ipconfig /flushdns` hoặc khởi động lại trình duyệt.

## Hoàn tác (Rollback)
Nhấp vào **Turn OFF** trong GUI, hoặc chạy PowerShell với quyền Quản trị viên.

Chế độ VNPT:
```powershell
Set-Location 'C:\Users\MSI VN\Desktop\VNPT-Steam-Bypass'
.\scripts\vnpt\Disable-VnptBypass.ps1
```
Chế độ Campus/HCMUS:
```powershell
Set-Location 'C:\Users\MSI VN\Desktop\VNPT-Steam-Bypass\scripts\campus'
.\Disable-CampusRelay.ps1
```
Thao tác này sẽ xóa các quy tắc NRPT, xóa khối hosts được quản lý và khôi phục mọi mục nhập hosts của Steam đã bị vô hiệu hóa bởi tập lệnh kích hoạt. Một bản sao lưu có gắn dấu thời gian của tệp `hosts` luôn được tạo trước mỗi lần thay đổi: nằm ngay cạnh các tập lệnh khi chạy từ PowerShell, hoặc nằm trong `%ProgramData%\VNPT-Steam-Bypass\scripts\backups` khi chạy từ GUI.

## Cách thức hoạt động
1. **Phân tách DNS bằng NRPT (NRPT split DNS).** Các quy tắc trong Bảng Chính sách Phân giải Tên (Name Resolution Policy Table) của Windows được thêm vào cho `steampowered.com`, `steamcommunity.com`, `steamstatic.com`, `steamgames.com`, `valvesoftware.com`, và các hậu tố tên miền khác của Steam/Valve. Chỉ những tên miền này mới được gửi tới Google DNS. Google DNS trả về các địa chỉ Akamai nội bộ trong mạng VNPT cho Store và Community, giúp tránh được đường truyền quốc tế đang bị lọc.
2. **Ghim trong tệp Hosts (Hosts pins).** Một vài tên miền (`store.steampowered.com`, `steamcommunity.com`, `steampowered.com`, `www.steampowered.com`, `steamgames.com`) được ghim vào một địa chỉ IP Akamai nội bộ đã được xác minh. Việc này là cần thiết vì Google DNS vẫn có thể trả về một máy chủ biên quốc tế cho các tên miền đó; việc kết nối tới máy chủ biên đó kèm theo SNI của Steam chính là thứ bị VNPT chặn (drop).
3. **Xử lý các mục cũ (Stale entries).** Các dòng `hosts` hiện có dành cho tên miền Steam sẽ được sao lưu và chuyển thành chú thích (commented out). Tập lệnh kích hoạt có tính lũy đẳng (idempotent) và có thể được chạy lại nhiều lần để làm mới các mục ghim khi địa chỉ IP của CDN luân chuyển (thay đổi).

## Kết quả dự kiến
Trên đường truyền VNPT đã thử nghiệm (TP.HCM, ngày 4 tháng 11 năm 2026):
| Điểm cuối (Endpoint) | Trước | Sau |
|---|---|---|
| `store.steampowered.com` | Hết thời gian chờ (timeout) TLS tới máy chủ biên Hồng Kông | HTTP `200` từ Akamai nội bộ |
| `steamcommunity.com` | Hết thời gian chờ (timeout) TLS tới máy chủ biên Hồng Kông | HTTP `200` từ Akamai nội bộ |
| `login.steampowered.com` | DNS `NXDOMAIN` trên trình phân giải VNPT | HTTP `302` |
| `api.steampowered.com` | DNS `NXDOMAIN` trên trình phân giải VNPT | HTTP `404` tại `/` (có thể truy cập được) |
| `cdn.akamai.steamstatic.com` | DNS `NXDOMAIN` trên trình phân giải VNPT | HTTP `200` |
| `example.com` (đối chứng không phải Steam) | bình thường | bình thường, cùng trình phân giải và đường truyền |

## Làm mới và khắc phục sự cố
- **IP của CDN luân chuyển (thay đổi).** Nếu sau này Steam bị lỗi trở lại, hãy nhấp vào **Repair bypass** trong GUI (hoặc chạy `.\scripts\vnpt\Enable-VnptBypass.ps1`). Công cụ sẽ xác thực lại các IP ứng viên và ghi lại khối được quản lý.
- **Kiểm tra ngữ cảnh mạng.** Mỗi lần chạy kích hoạt, vô hiệu hóa và xác minh của các tập lệnh bypass đều ghi lại nhật ký về giao diện mạng đang hoạt động, tên Wi-Fi (SSID), IP nội bộ, cổng mặc định (gateway) và máy chủ DNS; quá trình kích hoạt/xác minh cũng ghi lại cả IP công cộng/ASN. Điều này giúp dễ dàng nhận biết khi máy tính đang ở trên một mạng khác với mạng mà công cụ bypass này hướng tới.
- **Chú ý thông báo `Steam SNI reset`.** Nếu bảng ứng viên báo cáo ghi chú đó, hoặc `scripts\vnpt\Test-VnptBypass.ps1` kết thúc với kết luận "Steam TLS reset", thì mạng hiện tại đang ngắt (reset) quá trình bắt tay TLS của Steam bất kể địa chỉ IP đích là gì. Việc thay đổi DNS và tệp hosts không thể khắc phục được điều đó; hãy sử dụng một mạng cho phép kết nối TLS của Steam (chẳng hạn như đường truyền VNPT mà công cụ này nhắm tới). Tham số `-Force` sẽ vẫn cài đặt các quy tắc nhưng kết nối vẫn sẽ thất bại.
- **Khi rời khỏi mạng VNPT.** Các mục ghim trỏ tới các nút Akamai nội bộ của VNPT. Hãy nhấp vào **Turn OFF** trong GUI (hoặc chạy `.\scripts\vnpt\Disable-VnptBypass.ps1`) khi bạn chuyển sang nhà mạng (ISP) khác. Việc chạy lại tập lệnh kích hoạt trên một mạng khác chỉ hữu ích nếu mạng đó cho phép kết nối TLS của Steam; các mục ghim dành riêng cho VNPT không thể giúp ích trên mạng trường học hoặc mạng doanh nghiệp có áp dụng bộ lọc SNI.
- **Kiểm tra trạng thái:**
  ```powershell
  Get-DnsClientNrptRule | Where-Object Comment -eq 'VNPT-Steam-Bypass' |
      Select-Object Namespace, NameServers
  Select-String -Path "$env:SystemRoot\System32\drivers\etc\hosts" -Pattern 'VNPT-Steam-Bypass'
  ```
- **Steam vẫn lỗi trong ứng dụng (client) nhưng hoạt động bình thường trên trình duyệt.** Hãy đảm bảo Steam không được cấu hình để sử dụng proxy/VPN, thoát hoàn toàn Steam khỏi khay hệ thống (system tray), và khởi động lại ứng dụng. Một số bản dựng (builds) của Steam lưu bộ nhớ đệm (cache) DNS cho toàn bộ phiên làm việc.
- **Trình duyệt vẫn bị lỗi.** Hãy tắt tính năng Secure DNS / DoH riêng của trình duyệt (Chrome/Edge: Settings → Privacy → Use secure DNS; Firefox: Network Settings → Enable DNS over HTTPS). Một trình duyệt sử dụng trình phân giải DoH của riêng nó sẽ bỏ qua trình phân giải của Windows, NRPT và cả tệp hosts.
- **Khi đang bật VPN.** Hãy tắt bypass trước (nhấp vào **Turn OFF** trong GUI, hoặc chạy `.\scripts\vnpt\Disable-VnptBypass.ps1`), bởi vì các quy tắc NRPT có thể ghi đè lên DNS của VPN đối với các tên miền Steam và gây ra rò rỉ DNS (DNS leak) hoặc làm hỏng đường hầm kết nối (broken tunnel).

## Lưu ý (Caveats)
- Đây là giải pháp khắc phục bằng DNS/hosts đối với lệnh chặn được quan sát thấy trên một đường truyền VNPT. Nó không thể vượt qua các lệnh chặn ở cấp độ IP hoặc cấp độ giao thức trong tương lai.
- Đường truyền được thử nghiệm không có tuyến IPv6 hoạt động; các mục ghim đều là IPv4. Nếu đường truyền của bạn có IPv6 đang hoạt động, ứng dụng khách có thể ưu tiên các bản ghi AAAA và đi theo một đường truyền khác. Hãy chạy kiểm tra `scripts\vnpt\Test-VnptBypass.ps1` sau bất kỳ thay đổi nào liên quan đến IPv6.
- Công cụ chỉ thay đổi trên máy tính Windows này. Nó không sửa đổi bộ định tuyến (router) hay các thiết bị khác.
- Đây không phải là một công cụ bảo mật quyền riêng tư và không mã hóa lưu lượng truy cập Steam. Các tên miền DNS của Steam được gửi tới Google DNS thay vì trình phân giải hiện tại của bạn; các tên miền khác vẫn giữ nguyên trên trình phân giải hiện có. Store/Community được ghim trực tiếp và bỏ qua DNS hoàn toàn.
- Hãy sử dụng công cụ này phù hợp với luật pháp tại địa phương của bạn và điều khoản dịch vụ của Steam. Báo cáo gốc đi kèm chỉ ghi nhận bối cảnh chính sách; công cụ này chỉ thay đổi việc phân giải tên miền cục bộ trên máy.

## Chẩn đoán cơ sở (Baseline)
Công cụ chẩn đoán chỉ đọc (read-only) ban đầu vẫn có sẵn để sử dụng:
```powershell
.\Test-SteamVnpt.ps1 -TimeoutMs 5000 -OutFile .\result.md
```