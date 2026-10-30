import { tv } from "@/lib/utils";

// The site's card styles: square, hairline border.
export const card = tv({
  base: "group/card flex flex-col gap-(--card-spacing) overflow-hidden rounded-none border border-border bg-card py-(--card-spacing) text-13 text-card-foreground [--card-spacing:1.3125rem] has-data-[slot=card-footer]:pb-0 has-[>img:first-child]:pt-0 data-[size=sm]:[--card-spacing:0.8125rem] data-[size=sm]:has-data-[slot=card-footer]:pb-0",
  variants: {
    size: {
      default: "",
      sm: "",
    },
  },
  defaultVariants: {
    size: "default",
  },
});

export const cardAction = tv({
  base: "col-start-2 row-span-2 row-start-1 self-start justify-self-end",
});

export const cardContent = tv({
  base: "px-(--card-spacing)",
});

export const cardDescription = tv({
  base: "text-13 text-muted-foreground",
});

export const cardFooter = tv({
  base: "flex items-center rounded-b-8 border-t bg-muted/50 p-(--card-spacing)",
});

export const cardHeader = tv({
  base: "group/card-header @container/card-header grid auto-rows-min items-start gap-5 px-(--card-spacing) has-data-[slot=card-action]:grid-cols-[1fr_auto] has-data-[slot=card-description]:grid-rows-[auto_auto] [.border-b]:pb-(--card-spacing)",
});

export const cardTitle = tv({
  base: "font-heading text-16 leading-snug font-medium group-data-[size=sm]/card:text-13",
});
