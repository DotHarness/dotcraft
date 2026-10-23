import { useSkillDetailStore } from '../stores/skillDetailStore'

export function openSkill(name: string): void {
  useSkillDetailStore.getState().open(name)
}
