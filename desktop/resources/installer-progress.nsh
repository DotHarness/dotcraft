# At this hook SpiderBanner is ready and the stock app-check macros are available.
!macro customCheckAppRunning
  ${IfNot} ${Silent}
    Call CenterInstallStatus
  ${EndIf}
  !insertmacro IS_POWERSHELL_AVAILABLE
  !insertmacro _CHECK_APP_RUNNING
!macroend

Function CenterInstallStatus
  System::Store S
  FindWindow $0 "#32770" "" $HWNDPARENT
  FindWindow $0 "#32770" "" $HWNDPARENT $0
  GetDlgItem $1 $0 1000 ; SpiderBanner status label
  GetDlgItem $2 $0 1025 ; SpiderBanner icon
  ${If} $1 != 0
  ${AndIf} $2 != 0
    System::Call '*(i, i, i, i) p.r3'
    System::Call 'user32::GetWindowRect(p r2, p r3)'
    System::Call 'user32::MapWindowPoints(p 0, p r0, p r3, i 2)'
    System::Call '*$3(i, i.r4, i, i.r5)'
    System::Call 'user32::GetWindowRect(p r1, p r3)'
    System::Call 'user32::MapWindowPoints(p 0, p r0, p r3, i 2)'
    System::Call '*$3(i.r2, i.r6, i, i.r7)'
    System::Free $3
    IntOp $7 $7 - $6
    IntOp $5 $5 + $4
    IntOp $5 $5 - $7
    IntOp $5 $5 / 2
    # The robot's body carries more visual weight than its antenna.
    IntOp $6 $7 + 4
    IntOp $6 $6 / 8
    IntOp $5 $5 + $6
    System::Call 'user32::SetWindowPos(p r1, p 0, i r2, i r5, i 0, i 0, i 0x15)' ; NOSIZE | NOZORDER | NOACTIVATE
  ${EndIf}
  System::Store L
FunctionEnd
