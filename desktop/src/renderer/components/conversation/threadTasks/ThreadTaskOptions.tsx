import { useEffect, useLayoutEffect, useRef, useState, type JSX } from 'react'
import type { ThreadTaskStartOptions } from '../../../../shared/threadTasks'
import { useT } from '../../../contexts/LocaleContext'
import { useConfigValue } from '../../../stores/configStore'
import { useThreadStore } from '../../../stores/threadStore'
import {
  resolveConcreteApprovalPolicyFromWorkspaceDefault,
  resolveVisibleApprovalPolicy
} from '../../../utils/workspaceCoreConfig'
import { Button } from '../../ui/Button'
import { selectedWorktreeEnvironment } from '../../projectActions/worktreeEnvironments'
import { ApprovalPolicyPicker, type VisibleApprovalPolicy } from '../ApprovalPolicyPicker'
import { composerModelPillStyle } from '../ComposerShell'
import { ComposerWorkspaceFooter } from '../ComposerWorkspaceFooter'
import { ModelPicker } from '../ModelPicker'
import { useComposerModelControls } from '../useComposerModelControls'
import css from './ThreadTaskCard.module.css'

export interface ThreadTaskOptionsHandle {
  read(): Promise<ThreadTaskStartOptions>
}

interface ThreadTaskOptionsProps {
  workspacePath: string
  optionsRef: { current: ThreadTaskOptionsHandle | null }
  onOpenChange?: (open: boolean) => void
}

export function ThreadTaskOptions({ workspacePath, optionsRef, onOpenChange }: ThreadTaskOptionsProps): JSX.Element {
  const t = useT()
  const model = useComposerModelControls({ mode: 'detached' })
  const threadPolicy = useThreadStore((s) => s.activeThread?.configuration?.approvalPolicy)
  const workspaceDefault = resolveConcreteApprovalPolicyFromWorkspaceDefault(useConfigValue('Permissions.DefaultApprovalPolicy'))
  const inheritedApproval = resolveVisibleApprovalPolicy(threadPolicy, workspaceDefault)
  const [chosenApproval, setApproval] = useState<VisibleApprovalPolicy | null>(null)
  const approval = chosenApproval ?? inheritedApproval
  const [workLocation, setWorkLocation] = useState<'local' | 'worktree'>('local')
  const [baseRef, setBaseRef] = useState<string | null>(null)
  const [branchName, setBranchName] = useState<string | null>(null)
  const [open, setOpen] = useState(false)
  const [modelTouched, setModelTouched] = useState(false)
  const [opensUp, setOpensUp] = useState(false)
  const rootRef = useRef<HTMLSpanElement>(null)
  const popoverRef = useRef<HTMLDivElement>(null)

  optionsRef.current = {
    async read() {
      const environment = workLocation === 'worktree'
        ? await selectedWorktreeEnvironment(workspacePath).catch(() => null)
        : null
      const reasoning = model.reasoningValue
      return {
        workLocation,
        approvalPolicy: approval,
        ...(baseRef ? { baseRef } : {}),
        ...(branchName ? { branchName } : {}),
        ...(environment ? { environmentPath: environment.path } : {}),
        ...(modelTouched ? {
          providerId: model.providerId || undefined,
          model: model.modelName,
          ...(reasoning !== 'default' && reasoning !== 'off' ? { reasoningEffort: reasoning } : {})
        } : {})
      }
    }
  }

  useEffect(() => { onOpenChange?.(open) }, [open, onOpenChange])

  useLayoutEffect(() => {
    if (!open) {
      setOpensUp(false)
      return
    }
    const popover = popoverRef.current
    const root = rootRef.current
    if (!popover || !root) return
    const visibleBottom = scrollParentBottom(root)
    const above = root.getBoundingClientRect().top - scrollParentTop(root)
    setOpensUp(popover.getBoundingClientRect().bottom > visibleBottom && above > popover.offsetHeight)
  }, [open])

  useEffect(() => {
    if (!open) return
    const onKey = (event: KeyboardEvent): void => {
      if (event.key !== 'Escape') return
      event.preventDefault()
      event.stopPropagation()
      setOpen(false)
    }
    const onPointer = (event: PointerEvent): void => {
      const target = event.target as Element
      if (rootRef.current?.contains(target) || target.closest('[role="menu"], [role="listbox"], [role="dialog"]')) return
      setOpen(false)
    }
    window.addEventListener('keydown', onKey, true)
    window.addEventListener('pointerdown', onPointer)
    return () => {
      window.removeEventListener('keydown', onKey, true)
      window.removeEventListener('pointerdown', onPointer)
    }
  }, [open])

  const changed = [
    workLocation === 'worktree' ? t('workspaceFooter.newWorktree') : null,
    approval !== inheritedApproval
      ? t(approval === 'autoApprove' ? 'composer.approval.fullAccess.label' : 'composer.approval.prompt.label')
      : null,
    modelTouched ? model.modelName : null
  ].filter(Boolean).join(' · ')

  return (
    <span ref={rootRef} className={css.lead}>
      {changed ? <span className={css.where}>{changed}</span> : null}
      <Button variant="ghost" size="sm" aria-expanded={open} onClick={() => setOpen(!open)}>
        {t('threadTask.options')}
      </Button>
      {open ? (
        <div ref={popoverRef} className={`${css.popover} ${css.optionsPopover}`} data-opens-up={opensUp || undefined} role="dialog" aria-label={t('threadTask.options')}>
          <span className={css.optionRow}>
            <span>{t('threadTask.permissions')}</span>
            <ApprovalPolicyPicker value={approval} onChange={setApproval} />
          </span>
          <span className={css.optionRow}>
            <span>{t('threadTask.model')}</span>
            <ModelPicker
              providerId={model.providerId}
              modelName={model.modelName}
              modelOptions={model.modelOptions}
              modelCatalog={model.modelCatalog}
              reasoningValue={model.reasoningValue}
              speedValue={model.speedValue}
              loading={model.modelLoading}
              unsupported={model.modelListUnsupportedEndpoint}
              modelListReady={!model.modelLoading && !model.modelListUnsupportedEndpoint && !model.modelCatalogError && model.modelOptions.length > 0}
              errorMessage={model.modelCatalogError ? (model.modelCatalogErrorMessage || t('composer.modelListError')) : null}
              disabled={model.modelDisabled}
              onChange={(next) => { setModelTouched(true); model.onModelChange(next) }}
              allowDefaultModel={false}
              onReasoningChange={(next) => { setModelTouched(true); model.onReasoningChange(next) }}
              onSpeedChange={model.onSpeedChange}
              onRetry={model.onModelCatalogRetry}
              triggerStyle={composerModelPillStyle('var(--composer-footer-highlight)')}
            />
          </span>
          <span className={css.optionRow}>
            <span>{t('threadTask.where')}</span>
            <ComposerWorkspaceFooter
              workspacePath={workspacePath}
              mode={workLocation}
              variant="welcome"
              baseRef={baseRef}
              worktreeBranchName={branchName}
              onWelcomeModeChange={setWorkLocation}
              onBaseRefChange={setBaseRef}
              onWorktreeBranchNameChange={setBranchName}
            />
          </span>
        </div>
      ) : null}
    </span>
  )
}

function scrollParent(element: HTMLElement): HTMLElement | null {
  for (let node = element.parentElement; node; node = node.parentElement) {
    const overflow = getComputedStyle(node).overflowY
    if (overflow === 'auto' || overflow === 'scroll') return node
  }
  return null
}

function scrollParentBottom(element: HTMLElement): number {
  return scrollParent(element)?.getBoundingClientRect().bottom ?? window.innerHeight
}

function scrollParentTop(element: HTMLElement): number {
  return scrollParent(element)?.getBoundingClientRect().top ?? 0
}
