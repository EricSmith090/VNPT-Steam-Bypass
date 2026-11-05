# VNPT-Steam-Bypass
Một giải pháp khắc phục có mục tiêu dành cho việc VNPT chặn Steam được ghi nhận trong tài liệu [`VNPT-Steam-Report.md`](VNPT-Steam-Report.md). Công cụ này sử dụng **phân tách DNS (split DNS) kết hợp với một danh sách ghim nhỏ trong tệp hosts** thay vì dùng VPN hoặc proxy.
Để có trải nghiệm bật/tắt chỉ bằng một cú nhấp chuột, hãy sử dụng giao diện đồ họa (GUI) độc lập tại `dist\VNPT-Steam-Bypass.exe`; các tập lệnh PowerShell vẫn có sẵn để sử dụng qua dòng lệnh.

## Công cụ này thay đổi những gì
| Lớp | Thay đổi | Phạm vi |
|---|---|---|
| DNS | Các quy tắc NRPT của Windows chỉ gửi các tên miền của Steam/Valve tới `8.8.8.8` / `8.8.4.4` | Chỉ các tên miền Steam |
| Hosts | Một khối nhỏ được quản lý sẽ ghim một vài tên miền mà Google vẫn phân giải tới máy chủ biên (edge) quốc tế bị lọc | Chỉ các tên miền Steam |
| Mọi thứ khác | Giữ nguyên | Tất cả các DNS khác và mọi lưu lượng mạng khác |

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
| `dist\VNPT-Steam-Bypass.exe` | Tệp GUI độc lập đã được biên dịch (sau khi chạy `Build-Exe.ps1`) |
| `Build-Exe.ps1` | Biên dịch tệp EXE giao diện đồ họa và nhúng các tập lệnh PowerShell vào trong |
| `src\SteamBypassGui.cs` | Mã nguồn ứng dụng GUI |
| `src\UiControls.cs` | Các điều khiển WinForms giao diện tối (dark-theme) |
| `src\app.manifest` | Yêu cầu quyền Quản trị viên (Administrator) cho tệp EXE |
| `Enable-SteamBypass.ps1` | Thêm các quy tắc NRPT và khối hosts được quản lý; sao lưu tệp `hosts` trước khi thực hiện |
| `Disable-SteamBypass.ps1` | Xóa các quy tắc/khối đã thêm và khôi phục các dòng hosts liên quan đến Steam trước đó |
| `Test-SteamBypass.ps1` | Xác minh DNS, NRPT, tệp hosts và các phản hồi HTTPS thực tế |
| `SteamBypass.Common.ps1` | Các hàm hỗ trợ dùng chung và danh sách tên miền |
| `Test-SteamVnpt.ps1` | Tập lệnh chẩn đoán cơ sở chỉ đọc (read-only) ban đầu từ báo cáo |

## Giao diện đồ họa - GUI (khuyên dùng)
`dist\VNPT-Steam-Bypass.exe` là một ứng dụng giao diện đồ họa Windows độc lập cho cùng một phương pháp vượt rào (bypass) này. Nó nhúng sẵn bốn tập lệnh PowerShell, vì vậy tệp EXE có thể được sao chép tới bất kỳ đâu trên máy tính này và tự chạy độc lập.
1. Nhấp đúp vào tệp EXE và chấp nhận hộp thoại UAC.
2. Thẻ trạng thái sẽ hiển thị **BYPASS IS ON** (Đang bật), **OFF** (Đang tắt), hoặc **PARTIAL** (Bật một phần).
3. Nhấp vào **Turn ON** / **Turn OFF** / **Repair bypass** để thay đổi trạng thái.
4. Nhấp vào **Test connection** để chạy các bước kiểm tra tương tự như `Test-SteamBypass.ps1`.
5. Nhấp vào **Open data folder** để xem thư mục `%ProgramData%\VNPT-Steam-Bypass`, nơi tệp EXE giải nén các tập lệnh được nhúng và ghi nhật ký (logs) cùng các bản sao lưu tệp hosts.

GUI yêu cầu quyền quản trị viên vì nó thay đổi các quy tắc NRPT và tệp `hosts`. Các tập lệnh sẽ được giải nén lại mỗi khi tệp EXE khởi động, do đó tệp EXE luôn sử dụng đúng các tập lệnh đã được nhúng tại thời điểm nó được biên dịch.

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
.\Enable-SteamBypass.ps1 -DryRun
# Áp dụng
.\Enable-SteamBypass.ps1
# Xác minh
.\Test-SteamBypass.ps1
```
Thoát hoàn toàn Steam, bao gồm cả biểu tượng ở khay hệ thống (system tray), sau đó khởi động lại. Nếu trình duyệt vẫn hiển thị trang cũ, hãy chạy lệnh `ipconfig /flushdns` hoặc khởi động lại trình duyệt.

## Hoàn tác (Rollback)
Nhấp vào **Turn OFF** trong GUI, hoặc chạy PowerShell với quyền Quản trị viên:
```powershell
Set-Location 'C:\Users\MSI VN\Desktop\VNPT-Steam-Bypass'
.\Disable-SteamBypass.ps1
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
- **IP của CDN luân chuyển (thay đổi).** Nếu sau này Steam bị lỗi trở lại, hãy nhấp vào **Repair bypass** trong GUI (hoặc chạy `.\Enable-SteamBypass.ps1`). Công cụ sẽ xác thực lại các IP ứng viên và ghi lại khối được quản lý.
- **Kiểm tra ngữ cảnh mạng.** Mỗi lần chạy kích hoạt, vô hiệu hóa và xác minh của các tập lệnh bypass đều ghi lại nhật ký về giao diện mạng đang hoạt động, tên Wi-Fi (SSID), IP nội bộ, cổng mặc định (gateway) và máy chủ DNS; quá trình kích hoạt/xác minh cũng ghi lại cả IP công cộng/ASN. Điều này giúp dễ dàng nhận biết khi máy tính đang ở trên một mạng khác với mạng mà công cụ bypass này hướng tới.
- **Chú ý thông báo `Steam SNI reset`.** Nếu bảng ứng viên báo cáo ghi chú đó, hoặc `Test-SteamBypass.ps1` kết thúc với kết luận "Steam TLS reset", thì mạng hiện tại đang ngắt (reset) quá trình bắt tay TLS của Steam bất kể địa chỉ IP đích là gì. Việc thay đổi DNS và tệp hosts không thể khắc phục được điều đó; hãy sử dụng một mạng cho phép kết nối TLS của Steam (chẳng hạn như đường truyền VNPT mà công cụ này nhắm tới). Tham số `-Force` sẽ vẫn cài đặt các quy tắc nhưng kết nối vẫn sẽ thất bại.
- **Khi rời khỏi mạng VNPT.** Các mục ghim trỏ tới các nút Akamai nội bộ của VNPT. Hãy nhấp vào **Turn OFF** trong GUI (hoặc chạy `.\Disable-SteamBypass.ps1`) khi bạn chuyển sang nhà mạng (ISP) khác. Việc chạy lại tập lệnh kích hoạt trên một mạng khác chỉ hữu ích nếu mạng đó cho phép kết nối TLS của Steam; các mục ghim dành riêng cho VNPT không thể giúp ích trên mạng trường học hoặc mạng doanh nghiệp có áp dụng bộ lọc SNI.
- **Kiểm tra trạng thái:**
  ```powershell
  Get-DnsClientNrptRule | Where-Object Comment -eq 'VNPT-Steam-Bypass' |
      Select-Object Namespace, NameServers
  Select-String -Path "$env:SystemRoot\System32\drivers\etc\hosts" -Pattern 'VNPT-Steam-Bypass'
  ```
- **Steam vẫn lỗi trong ứng dụng (client) nhưng hoạt động bình thường trên trình duyệt.** Hãy đảm bảo Steam không được cấu hình để sử dụng proxy/VPN, thoát hoàn toàn Steam khỏi khay hệ thống (system tray), và khởi động lại ứng dụng. Một số bản dựng (builds) của Steam lưu bộ nhớ đệm (cache) DNS cho toàn bộ phiên làm việc.
- **Trình duyệt vẫn bị lỗi.** Hãy tắt tính năng Secure DNS / DoH riêng của trình duyệt (Chrome/Edge: Settings → Privacy → Use secure DNS; Firefox: Network Settings → Enable DNS over HTTPS). Một trình duyệt sử dụng trình phân giải DoH của riêng nó sẽ bỏ qua trình phân giải của Windows, NRPT và cả tệp hosts.
- **Khi đang bật VPN.** Hãy tắt bypass trước (nhấp vào **Turn OFF** trong GUI, hoặc chạy `.\Disable-SteamBypass.ps1`), bởi vì các quy tắc NRPT có thể ghi đè lên DNS của VPN đối với các tên miền Steam và gây ra rò rỉ DNS (DNS leak) hoặc làm hỏng đường hầm kết nối (broken tunnel).

## Lưu ý (Caveats)
- Đây là giải pháp khắc phục bằng DNS/hosts đối với lệnh chặn được quan sát thấy trên một đường truyền VNPT. Nó không thể vượt qua các lệnh chặn ở cấp độ IP hoặc cấp độ giao thức trong tương lai.
- Đường truyền được thử nghiệm không có tuyến IPv6 hoạt động; các mục ghim đều là IPv4. Nếu đường truyền của bạn có IPv6 đang hoạt động, ứng dụng khách có thể ưu tiên các bản ghi AAAA và đi theo một đường truyền khác. Hãy chạy kiểm tra `Test-SteamBypass.ps1` sau bất kỳ thay đổi nào liên quan đến IPv6.
- Công cụ chỉ thay đổi trên máy tính Windows này. Nó không sửa đổi bộ định tuyến (router) hay các thiết bị khác.
- Đây không phải là một công cụ bảo mật quyền riêng tư và không mã hóa lưu lượng truy cập Steam. Các tên miền DNS của Steam được gửi tới Google DNS thay vì trình phân giải hiện tại của bạn; các tên miền khác vẫn giữ nguyên trên trình phân giải hiện có. Store/Community được ghim trực tiếp và bỏ qua DNS hoàn toàn.
- Hãy sử dụng công cụ này phù hợp với luật pháp tại địa phương của bạn và điều khoản dịch vụ của Steam. Báo cáo gốc đi kèm chỉ ghi nhận bối cảnh chính sách; công cụ này chỉ thay đổi việc phân giải tên miền cục bộ trên máy.

## Chẩn đoán cơ sở (Baseline)
Công cụ chẩn đoán chỉ đọc (read-only) ban đầu vẫn có sẵn để sử dụng:
```powershell
.\Test-SteamVnpt.ps1 -TimeoutMs 5000 -OutFile .\result.md
```