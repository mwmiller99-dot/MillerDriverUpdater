## Miller Driver Updater 2.2.1

- **Setup no longer looks stuck when an antivirus removes the app.** Some antivirus programs (Malwarebytes, for
  example) remove new, unsigned programs on a machine-learning guess. Setup used to stop at that point with a window
  that looked like it was still working. Now it pauses on a clear message: add the install folder to the antivirus'
  allow list, click **Retry**, and Setup puts the file back and carries on. Cancel leaves everything as it was.
  The same applies to updates started from the app's Update button.

Install: download `MillerDriverUpdater-2.2.1-Setup.exe` below and run it. Windows may say "Windows protected your PC":
click *More info*, then *Run anyway* (the app is not code-signed yet). Copies on 2.2.0 offer this update themselves.