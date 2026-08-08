import oratorioIcon from '../../assets/oratorio/oratorio-icon.svg'

export function OratorioBrandMark(): JSX.Element {
  return (
    <span className="ora-board__logo" aria-hidden="true">
      <img src={oratorioIcon} alt="" />
      <svg className="ora-board__wand-overlay" viewBox="225 190 640 640" focusable="false">
        <g transform="translate(128 128) scale(.75)">
          <path className="ora-board__wand-aura" d="M808 606 896 294" />
          <ellipse className="ora-board__wand-orbit" cx="852" cy="450" rx="106" ry="35" transform="rotate(-74 852 450)" />
          <circle className="ora-board__wand-ring" cx="896" cy="294" r="48" />
          <circle className="ora-board__wand-ring ora-board__wand-ring--secondary" cx="896" cy="294" r="40" />
          <path className="ora-board__wand-body" d="M808 606 896 294" />
          <path className="ora-board__wand-core" d="M808 606 896 294" />
          <circle className="ora-board__wand-tip" cx="896" cy="294" r="42" />
          <g className="ora-board__wand-spark">
            <path d="M896 214v48" />
            <path d="M872 238h48" />
          </g>
          <g className="ora-board__wand-spark ora-board__wand-spark--secondary">
            <path d="M942 260v34" />
            <path d="M925 277h34" />
          </g>
        </g>
      </svg>
    </span>
  )
}
