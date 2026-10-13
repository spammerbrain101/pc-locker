import tkinter as tk
import ctypes
import sys
import os
import json
import hashlib
import subprocess
from ctypes import wintypes

# ── Config ──
if getattr(sys, "frozen", False):
    APP_DIR = os.path.dirname(sys.executable)
else:
    APP_DIR = os.path.dirname(os.path.abspath(__file__))
CONFIG_PATH = os.path.join(APP_DIR, "lock_config.json")

def load_config():
    if os.path.exists(CONFIG_PATH):
        with open(CONFIG_PATH, "r") as f:
            return json.load(f)
    return {}

def save_config(cfg):
    with open(CONFIG_PATH, "w") as f:
        json.dump(cfg, f)

def hash_password(pw):
    return hashlib.sha256(pw.encode()).hexdigest()

# ── Windows API ──
user32 = ctypes.windll.user32
kernel32 = ctypes.windll.kernel32

WH_KEYBOARD_LL = 13

class KBDLLHOOKSTRUCT(ctypes.Structure):
    _fields_ = [
        ("vkCode", wintypes.DWORD),
        ("scanCode", wintypes.DWORD),
        ("flags", wintypes.DWORD),
        ("time", wintypes.DWORD),
        ("dwExtraInfo", wintypes.ULONG),
    ]

CMPFUNC = ctypes.CFUNCTYPE(ctypes.c_int, wintypes.HINT, wintypes.WPARAM, ctypes.POINTER(KBDLLHOOKSTRUCT))
hook = None

def low_level_keyboard_handler(nCode, wParam, lParam):
    kb = lParam.contents
    if kb.vkCode in (91, 92):
        return 1
    if kb.flags & 0x20:
        if kb.vkCode in (9, 27, 115, 87):
            return 1
    if user32.GetAsyncKeyState(0x11) & 0x8000:
        if kb.vkCode in (27, 115, 87, 70):
            return 1
    return user32.CallNextHookEx(hook, nCode, wParam, lParam)

def install_hook():
    global hook
    ptr = CMPFUNC(low_level_keyboard_handler)
    hook = user32.SetWindowsHookExA(WH_KEYBOARD_LL, ptr, kernel32.GetModuleHandleW(None), 0)
    return ptr

def uninstall_hook():
    global hook
    if hook:
        user32.UnhookWindowsHookEx(hook)
        hook = None

def disable_task_manager(disable=True):
    try:
        key = r"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System"
        if disable:
            subprocess.run(["reg", "add", key, "/v", "DisableTaskMgr", "/t", "REG_DWORD", "/d", "1", "/f"],
                         capture_output=True)
        else:
            subprocess.run(["reg", "delete", key, "/v", "DisableTaskMgr", "/f"],
                         capture_output=True)
    except:
        pass

# ── Locker GUI ──
class Locker:
    def __init__(self, password_hash):
        self.password_hash = password_hash
        self.attempts = 0
        self.root = tk.Tk()
        self.root.overrideredirect(True)
        self.root.attributes("-topmost", True)
        self.root.attributes("-fullscreen", True)
        self.root.configure(bg="black")
        self.root.focus_force()

        # Center frame
        frame = tk.Frame(self.root, bg="black")
        frame.place(relx=0.5, rely=0.5, anchor="center")

        # Shield icon
        tk.Label(frame, text="🛡️", font=("Segoe UI Emoji", 72), bg="black", fg="white").pack(pady=(0, 15))

        # Title: The Mager
        tk.Label(frame, text="The Mager", font=("Segoe UI", 36, "bold"), bg="black", fg="#00d4ff").pack()

        # Subtitle
        tk.Label(frame, text="Enter password to unlock", font=("Segoe UI", 14), bg="black", fg="#888888").pack(pady=(5, 25))

        # Password entry
        self.entry = tk.Entry(frame, font=("Segoe UI", 18), show="●", width=25, bg="#1a1a1a", fg="white",
                             insertbackground="white", relief="flat", justify="center")
        self.entry.pack(pady=(0, 10), ipady=8)
        self.entry.focus_set()

        # Error label
        self.error_label = tk.Label(frame, text="", font=("Segoe UI", 12), bg="black", fg="#ff4444")
        self.error_label.pack()

        # Attempts label
        self.attempts_label = tk.Label(frame, text="", font=("Segoe UI", 10), bg="black", fg="#555555")
        self.attempts_label.pack(pady=(5, 0))

        # Bind keys
        self.root.bind("<Return>", self.check_password)
        self.root.bind("<Escape>", lambda e: None)
        self.root.bind("<Alt-F4>", lambda e: None)
        self.entry.bind("<Return>", self.check_password)

        self.root.after(100, self.enforce_focus)

    def enforce_focus(self):
        try:
            self.root.lift()
            self.root.attributes("-topmost", True)
            if not self.entry.get():
                self.entry.focus_set()
        except:
            pass
        self.root.after(200, self.enforce_focus)

    def check_password(self, event=None):
        entered = self.entry.get()
        if hash_password(entered) == self.password_hash:
            self.unlock()
        else:
            self.attempts += 1
            self.entry.delete(0, tk.END)
            self.error_label.config(text="❌ Wrong password")
            self.attempts_label.config(text=f"Failed attempts: {self.attempts}")
            x = self.root.winfo_x()
            for i in range(3):
                self.root.geometry(f"+{x+5}+0")
                self.root.update()
                self.root.geometry(f"+{x-5}+0")
                self.root.update()
            self.root.geometry(f"+{x}+0")

    def unlock(self):
        disable_task_manager(False)
        uninstall_hook()
        self.root.destroy()

    def run(self):
        disable_task_manager(True)
        ptr = install_hook()
        msg = wintypes.MSG()
        while True:
            ret = user32.PeekMessageW(ctypes.byref(msg), 0, 0, 0, 1)
            if ret:
                user32.TranslateMessage(ctypes.byref(msg))
                user32.DispatchMessageW(ctypes.byref(msg))
            try:
                self.root.update()
            except:
                break
        disable_task_manager(False)
        uninstall_hook()

# ── Setup ──
def setup_password():
    cfg = load_config()
    if "password_hash" in cfg:
        return cfg["password_hash"]
    
    print("=== The Mager - Setup ===")
    print("No password set. Create one now.")
    while True:
        pw = input("Enter password: ").strip()
        if len(pw) < 4:
            print("Password must be at least 4 characters")
            continue
        pw2 = input("Confirm password: ").strip()
        if pw != pw2:
            print("Passwords don't match. Try again.")
            continue
        cfg["password_hash"] = hash_password(pw)
        save_config(cfg)
        print("Password saved. Locking...")
        return cfg["password_hash"]

def change_password():
    cfg = load_config()
    if "password_hash" not in cfg:
        print("No password set yet. Run without --change first.")
        return
    old = input("Enter current password: ").strip()
    if hash_password(old) != cfg["password_hash"]:
        print("Wrong password.")
        return
    while True:
        pw = input("Enter new password: ").strip()
        if len(pw) < 4:
            print("Password must be at least 4 characters")
            continue
        pw2 = input("Confirm new password: ").strip()
        if pw != pw2:
            print("Passwords don't match. Try again.")
            continue
        cfg["password_hash"] = hash_password(pw)
        save_config(cfg)
        print("Password changed.")
        return

def install_startup():
    exe = sys.executable if getattr(sys, "frozen", False) else os.path.abspath(__file__)
    key = r"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"
    subprocess.run(["reg", "add", key, "/v", "TheMager", "/t", "REG_SZ", "/d", f'"{exe}"', "/f"],
                 capture_output=True)
    print(f"Installed to startup: {exe}")

def remove_startup():
    key = r"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"
    subprocess.run(["reg", "delete", key, "/v", "TheMager", "/f"],
                 capture_output=True)
    print("Removed from startup.")

if __name__ == "__main__":
    if len(sys.argv) > 1:
        if sys.argv[1] == "--change":
            change_password()
        elif sys.argv[1] == "--install":
            install_startup()
        elif sys.argv[1] == "--uninstall":
            remove_startup()
        elif sys.argv[1] == "--lock":
            pw_hash = load_config().get("password_hash")
            if pw_hash:
                locker = Locker(pw_hash)
                locker.run()
            else:
                print("No password set. Run without args first.")
        sys.exit(0)

    pw_hash = setup_password()
    locker = Locker(pw_hash)
    locker.run()
