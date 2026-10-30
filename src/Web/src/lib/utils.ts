import { createCn } from "cn/config"
import { createTV } from "tailwind-variants"

// The Fibonacci theme (global.css) names sizes by number. Without this, text-16 reads as a text colour and merging
// drops text-primary-foreground; radii and containers would not override each other.
const twMergeConfig = {
  extend: {
    theme: {
      text: ["13", "16", "21", "34", "55"],
      radius: ["2", "3", "5", "8", "13"],
      container: ["377", "610", "987"],
    },
  },
}

export const cn = createCn(twMergeConfig)
export const tv = createTV({ twMergeConfig })
