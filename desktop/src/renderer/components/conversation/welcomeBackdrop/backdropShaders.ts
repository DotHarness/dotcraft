export const MAX_TAPS = 6

const VERTEX = `#version 300 es
void main() {
  vec2 corner = vec2(float((gl_VertexID << 1) & 2), float(gl_VertexID & 2));
  gl_Position = vec4(corner * 2.0 - 1.0, 0.0, 1.0);
}
`

const HASH = `
uint pcg(uint v) {
  uint s = v * 747796405u + 2891336453u;
  uint w = ((s >> ((s >> 28u) + 4u)) ^ s) * 277803737u;
  return (w >> 22u) ^ w;
}
float unit(uint h) { return float(h >> 8u) * (1.0 / 16777216.0); }
`

// The lattice is offset so that integer coordinates stay positive before they become uints.
const NOISE = `
float lattice(ivec3 i, uint salt) {
  uvec3 u = uvec3(i + 65536);
  return unit(pcg(u.x ^ pcg(u.y ^ pcg(u.z ^ u_seed ^ salt)))) * 2.0 - 1.0;
}
float vnoise(vec3 p, uint salt) {
  vec3 fl = floor(p);
  vec3 f = p - fl;
  ivec3 i = ivec3(fl);
  vec3 w = f * f * (3.0 - 2.0 * f);
  return mix(
    mix(mix(lattice(i, salt), lattice(i + ivec3(1, 0, 0), salt), w.x),
        mix(lattice(i + ivec3(0, 1, 0), salt), lattice(i + ivec3(1, 1, 0), salt), w.x), w.y),
    mix(mix(lattice(i + ivec3(0, 0, 1), salt), lattice(i + ivec3(1, 0, 1), salt), w.x),
        mix(lattice(i + ivec3(0, 1, 1), salt), lattice(i + ivec3(1, 1, 1), salt), w.x), w.y),
    w.z);
}
`

const FIELD = `
float gustAt(vec2 at, float carried) {
  float sum = 0.0, amp = 1.0, norm = 0.0;
  vec2 size = u_wind.zw;
  for (int k = 0; k < 4; k++) {
    float fk = float(k);
    sum += amp * vnoise(vec3((at.x - carried * (1.0 + 0.12 * fk)) / size.x, at.y / size.y, u_time * (0.09 + 0.05 * fk) + 7.0 * fk), 20u + uint(k));
    norm += amp;
    amp *= 0.5;
    size *= vec2(0.5, 0.58);
  }
  float span = max(u_paneCss.x * 0.9, 480.0);
  float drift = 0.5 * span * vnoise(vec3(u_time * 0.045, at.y / (2.5 * u_wind.w), 7.0), 24u);
  return 1.7 * sum / norm + 0.3 * cos((at.x - carried + drift) * 6.2831853 / span);
}
float field(vec2 p, vec4 hand) {
  vec2 heading = vec2(cos(u_wind.x), sin(u_wind.x));
  vec2 across = vec2(-heading.y, heading.x);
  // Air that has been pushed shows here what was over there.
  vec2 from = p - hand.xy;
  vec2 bend = vec2(vnoise(vec3(from / 360.0, u_time * 0.04), 11u), vnoise(vec3(from / 360.0 + 9.3, u_time * 0.04), 12u));
  vec2 q = vec2(dot(from + 80.0 * bend, heading), dot(from + 80.0 * bend, across));
  float carried = u_wind.y * (u_time + 2.5 * vnoise(vec3(u_time * 0.08, 3.7, 0.0), 13u));
  vec2 riding = (q - vec2(0.85 * carried, 0.0)) / (1.1 * u_wind.w);
  vec2 eddy = vec2(vnoise(vec3(riding, u_time * 0.12), 14u), vnoise(vec3(riding + 31.7, u_time * 0.12), 15u));
  vec2 curled = q + 0.6 * (1.0 + 0.9 * min(hand.w, 1.5)) * u_wind.w * eddy;
  float now = gustAt(curled, carried) + hand.z;
  float ahead = gustAt(curled + vec2(56.0, 0.0), carried) + hand.z;
  float level = mix(0.25, 0.55, 0.5 + 0.5 * vnoise(vec3(0.0, 0.0, u_time * 0.035), 16u));
  float arriving = smoothstep(-0.05, 0.07, ahead - now);
  float lit = smoothstep(level - mix(0.05, 0.22, arriving), level + 0.2, now);
  float thread = vnoise(vec3((curled.x - carried * 1.6) / 140.0, curled.y / 28.0, u_time * 0.16), 30u);
  float streak = 1.0 - smoothstep(0.0, 0.5, abs(thread));
  return lit * lit * (0.6 + 0.4 * streak);
}

// A click is a blow: a front runs out and slows, with a trough and a weaker crest behind it, shoving the air outward.
// Returns the push in CSS pixels (xy), the wind raised or lowered (z) and how roused the air is (w); glow is the front's colour and its afterlight.
vec4 tapsAt(vec2 p, out vec2 glow) {
  vec4 sum = vec4(0.0);
  glow = vec2(0.0);
  for (int i = 0; i < ${MAX_TAPS}; i++) {
    if (i >= u_tapCount) break;
    vec4 tap = u_taps[i];
    float age = u_now - tap.z;
    vec2 away = p - tap.xy;
    float d = length(away);
    float behind = 620.0 * (1.0 - exp(-age / 1.3)) - d;
    float width = 85.0 + 60.0 * age;
    float front = smoothstep(-16.0, 10.0, behind) * exp(-behind * behind / (width * width));
    float swell = tap.w * exp(-age / 1.5) * front;
    sum.xy += (away / max(d, 1.0)) * (44.0 * swell);
    sum.z += 1.9 * swell * (0.25 + 0.75 * cos(behind * (6.2831853 / 130.0)));
    sum.w += swell;
    float trail = smoothstep(0.0, 70.0, behind) * exp(-max(behind, 0.0) / 260.0);
    glow.x += tap.w * (exp(-age / 1.0) * front + 0.7 * exp(-age / 1.3) * trail);
    glow.y += tap.w * (1.0 - exp(-age / 0.2)) * exp(-age / 1.2) * trail;
  }
  return sum;
}
`

const FIELD_FRAGMENT = `#version 300 es
precision highp float;
precision highp int;
uniform uint u_seed;
uniform float u_time, u_now, u_reveal;
uniform vec2 u_cellCss, u_paneCss;
uniform vec4 u_calm, u_wind;
uniform vec3 u_pointer;
uniform int u_tapCount;
uniform vec4 u_taps[${MAX_TAPS}];
out vec4 o;
${HASH}
${NOISE}
${FIELD}
void main() {
  vec2 p = (vec2(ivec2(gl_FragCoord.xy)) + 0.5) * u_cellCss;

  vec4 hand = vec4(0.0);
  vec2 glow = vec2(0.0);
  if (u_tapCount > 0) {
    // Read a little off true, so that a blow's outline is never a drawn circle.
    vec2 near = p + 24.0 * vec2(vnoise(vec3(p / 95.0, u_now * 0.12), 40u), vnoise(vec3(p / 95.0 + 9.1, u_now * 0.12 + 4.0), 41u));
    hand = tapsAt(near, glow);
  }
  float light = field(p, hand);

  float warm = 0.0;
  if (u_pointer.z > 0.0) {
    vec2 d = p - u_pointer.xy;
    float grain = smoothstep(-0.35, 0.35, vnoise(vec3(p / 16.0, u_now * 0.6), 31u));
    warm = u_pointer.z * exp(-dot(d, d) / 4900.0) * grain;
  }

  vec2 q = abs(p - u_calm.xy) - u_calm.zw;
  float outside = length(max(q, 0.0)) + min(max(q.x, q.y), 0.0);
  float calm = mix(0.05, 1.0, smoothstep(-40.0, 180.0, outside));
  float roused = min(1.0, hand.w);
  // What the hand does shows at once, however little of the wind has arrived, and reaches a little into the calm.
  float shown = max(u_reveal, min(1.0, 2.0 * hand.w));
  float stirred = mix(calm, 1.0, 0.6 * roused);
  float lit = max(light * stirred * shown, warm * mix(calm, 1.0, 0.6));
  o = vec4(lit, clamp(glow.x, 0.0, 1.0), clamp(hand.w, 0.0, 1.0), clamp(glow.y * stirred, 0.0, 1.0));
}
`

const GLYPH_FRAGMENT = `#version 300 es
precision highp float;
precision highp int;
uniform highp sampler2D u_field;
uniform highp sampler2D u_atlas;
uniform highp sampler2D u_sequence;
uniform int u_count, u_bufferHeight;
uniform ivec2 u_cell, u_grid;
uniform uint u_seed;
uniform float u_time, u_flick, u_fill, u_strength, u_glowStrength;
uniform vec3 u_ink, u_glow;
out vec4 o;
${HASH}
void main() {
  ivec2 px = ivec2(int(gl_FragCoord.x), u_bufferHeight - 1 - int(gl_FragCoord.y));
  ivec2 c = px / u_cell;
  if (c.x >= u_grid.x || c.y >= u_grid.y) { o = vec4(0.0); return; }
  vec4 f = texelFetch(u_field, c, 0);
  float light = min(1.0, f.r);
  if (light <= 0.004 && f.a <= 0.015) { o = vec4(0.0); return; }
  uint h = pcg((uint(c.x) * 0x9E3779B9u) ^ pcg(uint(c.y) ^ u_seed));
  if (unit(pcg(h ^ 0x2545F491u)) >= u_fill) { o = vec4(0.0); return; }
  uint pace = pcg(h);
  // A roused cell runs on through its set, and back as it settles.
  int steps = int(floor(u_time * u_flick * (0.6 + 0.8 * unit(pace)) + unit(pcg(pace)))) + int(floor(6.0 * f.b + unit(pcg(pace ^ 0x33u))));
  int g = int(texelFetch(u_sequence, ivec2((int(h >> 8u) + steps) % u_count, 0), 0).r * 255.0 + 0.5);
  ivec2 local = px - c * u_cell;
  float ink = texelFetch(u_atlas, ivec2((g & 15) * u_cell.x + local.x, (g >> 4) * u_cell.y + local.y), 0).r;
  if (ink <= 0.0) { o = vec4(0.0); return; }
  // Every cell glows with its neighbours, some a little more than others.
  vec2 glow = f.ga * (0.65 + 0.35 * unit(pcg(pace ^ 0x51u)));
  // The colour comes in whole and goes out quickly: halfway between it and the plain ink is only mud.
  float tint = smoothstep(0.04, 0.3, glow.x);
  float bright = light * mix(u_strength, u_glowStrength, tint);
  float left = 0.1 * u_glowStrength * glow.y;
  float a = ink * min(1.0, bright + left);
  o = vec4((mix(u_ink, u_glow, tint) * bright + u_glow * left) / (bright + left + 0.0001) * a, a);
}
`

export const BACKDROP_PROGRAMS = { vertex: VERTEX, field: FIELD_FRAGMENT, glyph: GLYPH_FRAGMENT }
