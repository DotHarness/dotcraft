export interface SpringParams {
  response: number
  dampingFraction: number
}

export interface Spring extends SpringParams {
  value: number
  target: number
  velocity: number
  force: number
  simulationTime: number
  scriptTime: number
}

const SIMULATION_STEP = 1 / 240
const FRAME = 1 / 60
const MAX_CATCH_UP = 1
const REST_THRESHOLD = 0.06

export function createSpring(value: number, target: number, params: SpringParams): Spring {
  return { ...params, value, target, velocity: 0, force: 0, simulationTime: 0, scriptTime: 0 }
}

export function snapSpring(spring: Spring, value: number): void {
  spring.value = value
  spring.target = value
  spring.velocity = 0
  spring.force = 0
  spring.simulationTime = 0
  spring.scriptTime = 0
}

export function retune(spring: Spring, params: SpringParams): void {
  spring.response = params.response
  spring.dampingFraction = params.dampingFraction
}

export function steerAngle(spring: Spring, angle: number): void {
  spring.target = spring.value + shortestTurn(spring.value, angle)
}

export function shortestTurn(from: number, to: number): number {
  let turn = to - from
  while (turn > 180) turn -= 360
  while (turn < -180) turn += 360
  return turn
}

export function stepSpring(spring: Spring, dt: number): void {
  const response = Math.max(0.001, spring.response)
  const stiffness = Math.min((Math.PI * 2) ** 2 / response ** 2, 1 / (2 * SIMULATION_STEP ** 2))
  const damping = Math.sqrt(stiffness) * 2 * spring.dampingFraction
  spring.scriptTime += Math.max(0, dt)
  if (spring.scriptTime - spring.simulationTime > MAX_CATCH_UP) spring.simulationTime = spring.scriptTime - FRAME
  while (spring.simulationTime < spring.scriptTime) {
    const half = SIMULATION_STEP / 2
    const velocity = spring.velocity + spring.force * half
    spring.value += velocity * SIMULATION_STEP
    spring.force = velocity * -damping + (spring.target - spring.value) * stiffness
    spring.velocity = velocity + spring.force * half
    spring.simulationTime += SIMULATION_STEP
  }
  if (isResting(spring)) spring.value = spring.target
}

function isResting(spring: Spring): boolean {
  if (Math.max(spring.velocity ** 2, spring.force ** 2) > REST_THRESHOLD ** 2) return false
  const tolerance = spring.target * 0.01
  return tolerance === 0 || (spring.target - spring.value) ** 2 <= tolerance ** 2
}

export function isSettled(spring: Spring): boolean {
  return spring.value === spring.target && isResting(spring)
}
