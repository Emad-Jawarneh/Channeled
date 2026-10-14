#!/usr/bin/env bash
cd /home/jawarneh/Channeled
git show wip/account-profiles:web/src/components/AppShell.module.css | sed -n '/^\.accountMenu/,/^@media/p' | head -n -1
echo '---MOBILE---'
git show wip/account-profiles:web/src/components/AppShell.module.css | sed -n '/accountDropdown/,/^}/p' | head -20
