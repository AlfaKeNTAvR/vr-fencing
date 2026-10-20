# Installing VR Fencing on the partner's Quest

A step-by-step guide for the Partner: the second Fencer, who joins Nikita's bouts from her own Quest 3S. Each step can be done by hand, or handed to your own Claude to walk through with you. The app is not in the Meta store, so it is installed from a PC over a USB cable ("sideloading").

You need:

- the Quest 3S, charged, and its USB-C cable (or any USB-C data cable; some charging-only cables carry no data);
- a Windows PC;
- the Meta Horizon app on your phone, signed in to the same Meta account as the headset;
- the APK file Nikita sends you (for example `vr-fencing-1.1.apk`).

## 1. Turn on Developer Mode (once)

1. Go to https://developers.meta.com/horizon/manage/organizations/ and sign in with the headset's Meta account. Create an organization (any name, for example your own name). Meta may ask you to verify the account with a phone number or a payment card; that is free.
2. In the Meta Horizon phone app: Menu > Devices > pick the Quest 3S > Headset settings > Developer Mode > turn it on.
3. Restart the headset.

## 2. Install adb on the PC (once)

adb is Google's small tool for talking to Android devices; the Quest runs Android.

1. Download "SDK Platform-Tools for Windows" from https://developer.android.com/tools/releases/platform-tools and unzip it, for example to `C:\platform-tools`.
2. Open PowerShell in that folder (in Explorer: click the address bar, type `powershell`, press Enter).
3. Check it runs: `.\adb.exe version`.

## 3. Connect the headset (each time)

1. Put on the headset and plug it into the PC with the cable.
2. In the headset, a prompt "Allow USB debugging?" appears. Tick "Always allow from this computer" and choose Allow. If no prompt shows, unplug and plug in again.
3. On the PC: `.\adb.exe devices`. It should list one device followed by the word `device`. If it says `unauthorized`, answer the prompt in the headset.

## 4. Install or update the app

Put the APK in the same folder as `adb.exe` and run (with the real file name):

```
.\adb.exe install --no-streaming -r -g vr-fencing-1.1.apk
```

It prints `Success` when done. The same command installs the first time and updates later; your settings stay.

- `--no-streaming`: the default "streamed" install sometimes fails silently on the Quest.
- `-r`: replace the installed version, keeping its data.
- `-g`: grant the app's permissions up front.

## 5. Open the app

In the headset: Library > the filter at the top > Unknown sources > VR Fencing. It runs in passthrough, so you see your room.

1. Allow the body tracking permission if asked.
2. Set up your Strip when the app asks (follow its prompts).
3. Open the Partner page from the Home menu. Choose Join and type the 6-character code (letters and digits) that Nikita reads to you, on the big keypad.

## If it does not connect

- "Different version": the screen shows both version numbers. Both headsets need the same build; ask Nikita for the matching APK and repeat step 4.
- "Could not join": the code was mistyped or Nikita's session closed. Ask for a new code.
- If the headset went to sleep during a session, the connection drops. Leave on the Partner page and join again with a new code.

## Talking during a bout

Try Discord voice on the headset with the app open in front. If Discord stops when the app is open, use a phone call with earbuds instead. Tell Nikita which one worked.

## What not to do

- Do not factory reset the headset or turn off Developer Mode; a reinstall would then need the steps above again.
- You never need to uninstall the app: step 4 updates it in place.
