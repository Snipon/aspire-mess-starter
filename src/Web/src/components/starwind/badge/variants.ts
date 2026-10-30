import { tv } from "@/lib/utils";

// The site's badge styles.
export const badge = tv({
  base: "group/badge inline-flex h-21 w-fit shrink-0 items-center justify-center gap-5 overflow-hidden rounded-3 border border-transparent px-8 py-2 text-13 font-medium whitespace-nowrap transition-all focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring has-data-[icon=inline-end]:pr-5 has-data-[icon=inline-start]:pl-5 aria-invalid:border-destructive aria-invalid:ring-destructive/20 dark:aria-invalid:ring-destructive/40 [&>svg]:pointer-events-none [&>svg]:size-13!",
  variants: {
    variant: {
      default: "bg-primary text-primary-foreground [a]:hover:bg-primary/80",
      secondary:
        "border-border bg-transparent text-foreground [a]:hover:bg-muted",
      destructive:
        "bg-destructive/10 text-destructive focus-visible:outline-destructive dark:bg-destructive/20 [a]:hover:bg-destructive/20",
      outline:
        "border-border text-foreground [a]:hover:bg-muted [a]:hover:text-muted-foreground",
      ghost:
        "hover:bg-muted hover:text-muted-foreground dark:hover:bg-muted/50",
      link: "text-primary underline-offset-4 hover:underline",
      // Highlights: tinted, never the only signal (always a text label).
      sun: "border-sun/50 bg-sun/15 text-sun-foreground [a]:hover:bg-sun/30",
      sea: "border-sea/50 bg-sea/15 text-sea-foreground [a]:hover:bg-sea/30",
      rose: "border-rose/50 bg-rose/15 text-rose-foreground [a]:hover:bg-rose/30",
      iris: "border-iris/50 bg-iris/15 text-iris-foreground [a]:hover:bg-iris/30",
    },
  },
  defaultVariants: {
    variant: "default",
  },
});
