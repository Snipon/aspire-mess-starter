import { tv } from "tailwind-variants";

// The site's separator styles (ported from its shadcn separator).
export const separator = tv({
  base: "shrink-0 bg-border data-horizontal:h-px data-horizontal:w-full data-vertical:w-px data-vertical:self-stretch",
});
