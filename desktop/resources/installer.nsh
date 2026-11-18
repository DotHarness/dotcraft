!macro customHeader
  ManifestDPIAware true

  !ifdef ONE_CLICK
    !ifndef BUILD_UNINSTALLER
      !include "${BUILD_RESOURCES_DIR}\installer-progress.nsh"
    !endif
  !endif
!macroend
