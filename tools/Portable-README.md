# دیدنی‌ها — بستهٔ قابل حمل ویندوز

پیش‌نیاز اجرا: Windows x64 و PowerShell 7. بسته مستقل است؛ نصب .NET SDK یا Node.js برای اجرا لازم نیست.

## شروع

ZIP را در پوشه‌ای با دسترسی نوشتن استخراج کنید. PowerShell را در پوشهٔ برنامه باز کنید:

```powershell
.\tools\Start-Didanihaa.ps1
```

ادمین در مرورگر پیش‌فرض باز می‌شود. در اولین اجرا didanihaa.json از نمونه ساخته می‌شود؛ تنظیمات موجود بازنویسی نمی‌شوند.

لاگ‌ها و وضعیت فرایندهای راه‌انداز در `temp/runtime` هستند. وضعیت نسخه‌های قدیمی از `temp/dev/processes.json` همان بسته خوانده می‌شود و ثبت بعدی در مسیر جدید انجام می‌شود؛ فایل و لاگ‌های قدیمی حفظ می‌شوند.

پاسخ سرویس باید با هویت همین پوشه مطابقت داشته باشد؛ نسخهٔ قدیمی یا سرویسِ پوشهٔ دیگر خودکار بازاستفاده نمی‌شود. پیش از جایگزینی فایل‌های برنامه، نسخهٔ در حال اجرا را با Stop متوقف کنید. خطای تنظیمات، فایل اصلی را بازنویسی نمی‌کند؛ در نبود فایل محلی، نمونهٔ معتبر یک بار کپی می‌شود.

اگر اجرای اسکریپت با Execution Policy مسدود شد، آن را فقط در یک فرایند PowerShell اجرا کنید:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File .\tools\Start-Didanihaa.ps1
```

برای کنترل دریافت فایل، SHA256 آرشیو را با SHA256SUMS همراه نسخه مقایسه کنید.

1. پوشهٔ منبع را در ادمین اضافه کنید.
2. فایل‌های شناسایی‌شده را بازبینی و موارد دلخواه را صریحاً منتشر کنید.
3. روی TV یا دستگاه دیگر شبکه، `http://<LAN-IP>:13521/` را باز کنید.

مدیریت در `/admin/` و نسخهٔ ساده در `/tv/simple` است. اسکن به معنی انتشار نیست. سازگاری پخش فایل اصلی به کدک و مرورگر بستگی دارد.

FFprobe و FFmpeg اختیاری‌اند. برنامه ابزار همراه را در ffmpeg/bin می‌جوید. برای استفاده از نصب جداگانه، داشبورد localhost را روی دستگاه میزبان باز کنید و از تنظیمات ابزارها مسیر را وارد کنید. مسیر خالی تشخیص خودکار را فعال می‌کند. FFprobe مشخصات پیشرفته و FFmpeg تصویر بندانگشتی JPEG را فراهم می‌کند. پخش از فایل اصلی است.

## توقف و به‌روزرسانی

```powershell
.\tools\Stop-Didanihaa.ps1
```

اگر Media به‌صورت Windows Service نصب شده، ابتدا آن را از داشبورد متوقف کنید، سپس Stop را اجرا کنید. از data و didanihaa.json پشتیبان بگیرید و فایل‌های ZIP جدید را با جایگزینی فایل‌های برنامه در همان پوشه استخراج کنید. بسته، didanihaa.json یا data شخصی ندارد. فایل‌های موجود خودتان را حذف نکنید. پس از پایان، Start را اجرا و در صورت استفاده، سرویس را از داشبورد راه‌اندازی کنید.

## شبکه و سرویس

نصب سرویس اختیاری از داشبورد و با UAC انجام می‌شود. حساب LocalService به مجوز خواندن پوشه‌های رسانه نیاز دارد. Start سرویس یا قاعدهٔ فایروال نصب نمی‌کند.

install مجوز خواندن/اجرا برای ریشه و نوشتن محدود برای `temp/runtime` را تنظیم می‌کند. سرویس متعلق به پوشهٔ دیگر تغییر داده نمی‌شود؛ اجرای عادی با پورت‌های آزاد مستقل است.

دسترسی شبکه در صورت نیاز با `tools/Enable-LanAccess.ps1` و تأیید UAC تنظیم می‌شود. برای نمایش عملیات بدون تغییر از `-WhatIf` استفاده کنید. قاعده فقط ورودی Control روی LAN را پوشش می‌دهد؛ Media روی loopback:13522 می‌ماند.

ادمین فعلاً login ندارد؛ فقط در شبکهٔ محلی مورد اعتماد استفاده کنید و پورت سرویس را روی اینترنت باز نکنید. کاتالوگ، تنظیمات، کش و گزارش‌ها داخل پوشهٔ برنامه می‌مانند.

مجوز برنامه MIT است. LICENSE، THIRD_PARTY_NOTICES و مجوزهای قلم/وابستگی‌ها را همراه برنامه نگه دارید.

## English quick start

Requires Windows x64 and PowerShell 7; no .NET SDK or Node.js installation is needed. Extract the ZIP into a writable folder and run `tools/Start-Didanihaa.ps1`. Review and explicitly publish media in administration, then open `http://<LAN-IP>:13521/` on a trusted LAN device. Use `tools/Stop-Didanihaa.ps1` before updates. Stop a registered Media service through the dashboard first. Back up and preserve `data/` and `didanihaa.json` when extracting a newer package over the same folder. Service installation and firewall setup are separate optional operations.
