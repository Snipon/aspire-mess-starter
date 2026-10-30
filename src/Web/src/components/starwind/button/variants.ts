import { tv } from "@/lib/utils";

// The site's button styles: square corners, ink default, signal for the one primary action per view.
export const button = tv({
  base: "group/button inline-flex shrink-0 items-center justify-center rounded-3 border border-transparent bg-clip-padding text-13 font-medium whitespace-nowrap transition-all outline-none select-none focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring focus-visible:outline-solid active:not-aria-[haspopup]:translate-y-px disabled:pointer-events-none disabled:opacity-50 aria-invalid:border-destructive aria-invalid:ring-3 aria-invalid:ring-destructive/20 dark:aria-invalid:border-destructive/50 dark:aria-invalid:ring-destructive/40 [&_svg]:pointer-events-none [&_svg]:shrink-0 [&_svg:not([class*='size-'])]:size-13",
  variants: {
    variant: {
      default: "bg-ink text-ink-foreground hover:bg-ink/85",
      signal: "bg-primary text-primary-foreground hover:bg-primary/90",
      outline:
        "border-input bg-transparent hover:bg-muted aria-expanded:bg-muted",
      secondary:
        "bg-card text-card-foreground hover:bg-muted aria-expanded:bg-secondary aria-expanded:text-secondary-foreground",
      ghost:
        "hover:bg-muted hover:text-foreground aria-expanded:bg-muted aria-expanded:text-foreground dark:hover:bg-muted/50",
      destructive:
        "bg-destructive/10 text-destructive hover:bg-destructive/20 focus-visible:outline-destructive dark:bg-destructive/20 dark:hover:bg-destructive/30",
      link: "text-primary underline decoration-1 underline-offset-4 hover:text-foreground",
    },
    size: {
      default:
        "h-34 gap-8 px-13 has-data-[icon=inline-end]:pr-8 has-data-[icon=inline-start]:pl-8",
      xs: "h-21 gap-5 rounded-[min(var(--radius-md),10px)] px-8 text-13 in-data-[slot=button-group]:rounded-5 has-data-[icon=inline-end]:pr-5 has-data-[icon=inline-start]:pl-5 [&_svg:not([class*='size-'])]:size-13",
      sm: "h-34 gap-5 rounded-3 px-13 text-13 in-data-[slot=button-group]:rounded-5 has-data-[icon=inline-end]:pr-5 has-data-[icon=inline-start]:pl-5 [&_svg:not([class*='size-'])]:size-13",
      lg: "h-55 gap-8 px-21 text-16 has-data-[icon=inline-end]:pr-8 has-data-[icon=inline-start]:pl-8",
      icon: "size-34 [&_svg:not([class*='size-'])]:size-21",
      "icon-xs":
        "size-21 rounded-[min(var(--radius-md),10px)] in-data-[slot=button-group]:rounded-5 [&_svg:not([class*='size-'])]:size-13",
      "icon-sm":
        "size-34 rounded-[min(var(--radius-md),12px)] in-data-[slot=button-group]:rounded-5",
      "icon-lg": "size-55 [&_svg:not([class*='size-'])]:size-21",
    },
  },
  defaultVariants: {
    variant: "default",
    size: "default",
  },
});
