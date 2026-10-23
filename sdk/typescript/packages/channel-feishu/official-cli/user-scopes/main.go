package main

import (
	"encoding/json"
	"os"
	"slices"

	"github.com/larksuite/cli/shortcuts"
)

func main() {
	domains := map[string][]string{}
	for _, shortcut := range shortcuts.AllShortcuts() {
		if shortcut.Risk != "read" || !shortcuts.IsShortcutServiceAvailable(shortcut.Service, "feishu") {
			continue
		}
		if len(shortcut.AuthTypes) > 0 && !slices.Contains(shortcut.AuthTypes, "user") {
			continue
		}
		for _, scope := range shortcut.DeclaredScopesForIdentity("user") {
			if !slices.Contains(domains[shortcut.Service], scope) {
				domains[shortcut.Service] = append(domains[shortcut.Service], scope)
			}
		}
	}
	if err := json.NewEncoder(os.Stdout).Encode(domains); err != nil {
		panic(err)
	}
}
