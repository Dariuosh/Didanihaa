# تغییرات دیدنی‌ها

## 0.1.0

- داشبورد فارسی برای معرفی منبع، اسکن، بازبینی و انتشار صریح رسانه‌ها.
- کتابخانهٔ پوشه‌محور و پلی‌لیست با ترتیب عددی، قبلی/بعدی، تکرار و پخش خودکار.
- پخش فایل اصلی صدا و ویدیو با کنترل مشترک، تمام‌صفحه و HTTP Range/HEAD.
- رابط TV، نسخهٔ سادهٔ مرورگر، علاقه‌مندی مشترک و پربازدیدهای محلی.
- مشخصات رسانه با FFprobe و JPEG ویدیو با FFmpeg اختیاری.
- ذخیره‌سازی محلی SQLite، ریشهٔ قابل انتقال و مدیریت اختیاری Windows Service.
- بستهٔ مستقل Windows x64، راه‌انداز PowerShell و تنظیمات نمونه برای نصب و به‌روزرسانی.

## اجرا

بستهٔ `Didanihaa-v0.1.0-win-x64.zip` را استخراج و با PowerShell 7 از پوشهٔ برنامه اجرا کنید:

```powershell
.\tools\Start-Didanihaa.ps1
```

پخش در `http://<LAN-IP>:13521/` و مدیریت در `/admin/` در دسترس است. SDK و Node.js برای اجرای بسته لازم نیستند. FFprobe/FFmpeg همراه بسته نیستند و در صورت نیاز جداگانه تنظیم می‌شوند.

برای به‌روزرسانی، برنامه و سرویس Media را متوقف و از data و didanihaa.json پشتیبان بگیرید. تنظیمات محلی و کاتالوگ را حفظ کنید. سرویس برای LAN مورد اعتماد است؛ ادمین ورود/گذرواژه ندارد. پخش به فرمت اصلی و قابلیت مرورگر وابسته است؛ transcoding، DLNA و Chromecast در این نسخه ارائه نمی‌شوند.

## English

The first portable Windows x64 release includes Persian administration, explicit media publication, folder playlists, original-file playback, a simple TV view, shared household favorites, local popularity, optional metadata/thumbnails and Windows Service management. Extract the ZIP and run `tools/Start-Didanihaa.ps1` using PowerShell 7. Preserve `data/` and `didanihaa.json` during updates. Use on a trusted LAN; administration has no login. FFmpeg/FFprobe are optional external tools. Transcoding, DLNA and Chromecast are not included.
