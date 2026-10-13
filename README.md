# PC Locker

Lightweight Windows PC locker. Once locked, **everything** is blocked — only your password unlocks it.

## What it blocks
- **Fullscreen black overlay** — nothing visible behind it
- **Keyboard hook** — Win key, Alt+Tab, Ctrl+Esc, Alt+F4 all blocked
- **Task Manager disabled** — Ctrl+Shift+Esc won't open
- **Focus enforcement** — window re-raises every 200ms, can't click behind it
- **Failed attempt counter** — tracks wrong password attempts

## Setup

1. Double-click **setup.bat** or run `python pclocker.py`
2. Enter a password (min 4 characters) — it gets SHA-256 hashed and saved
3. PC locks immediately

## Quick Lock
- Double-click **lock.bat** or run `python pclocker.py --lock`

## Commands

| Command | What it does |
|---------|-------------|
| `python pclocker.py` | First run: set password & lock. After: just lock |
| `python pclocker.py --lock` | Lock immediately (no prompt) |
| `python pclocker.py --change` | Change password (requires current password) |
| `python pclocker.py --install` | Add to Windows startup (auto-lock on boot) |
| `python pclocker.py --uninstall` | Remove from startup |

## To unlock
- Type your password and press Enter
- Task Manager re-enabled, keyboard hook removed, overlay closes

## Security notes
- Password stored as SHA-256 hash — never saved in plaintext
- Registry key disables Task Manager while locked, re-enables on unlock
- Low-level keyboard hook blocks system keys at the OS level
- Safe shutdown (unlock) restores everything — no permanent changes

## Requirements
- Python 3 with tkinter (comes standard on Windows)
- Run as Administrator for full effectiveness (keyboard hook + Task Manager block)
