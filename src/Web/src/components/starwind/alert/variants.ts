import { tv } from "tailwind-variants";

// The site's alert styles: square, with an ink or destructive left rule.
export const alert = tv({
  base: "group/alert relative grid w-full gap-0.5 rounded-none border border-l-4 border-border border-l-ink px-4 py-3 text-left text-sm has-data-[slot=alert-action]:relative has-data-[slot=alert-action]:pr-18 has-[>svg]:grid-cols-[auto_1fr] has-[>svg]:gap-x-2 *:[svg]:row-span-2 *:[svg]:translate-y-0.5 *:[svg]:text-current *:[svg:not([class*='size-'])]:size-4",
  variants: {
    variant: {
      default: "bg-card text-card-foreground",
      destructive:
        "border-destructive/40 border-l-destructive bg-card text-destructive *:data-[slot=alert-description]:text-destructive *:[svg]:text-current",
    },
  },
  defaultVariants: {
    variant: "default",
  },
});

export const alertTitle = tv({
  base: "font-semibold group-has-[>svg]/alert:col-start-2 [&_a]:underline [&_a]:decoration-1 [&_a]:underline-offset-4 [&_a]:hover:text-foreground",
});

export const alertDescription = tv({
  base: "text-sm text-balance text-muted-foreground md:text-pretty [&_a]:underline [&_a]:decoration-1 [&_a]:underline-offset-4 [&_a]:hover:text-foreground [&_p:not(:last-child)]:mb-4",
});
