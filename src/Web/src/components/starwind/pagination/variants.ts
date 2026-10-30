import { tv } from "tailwind-variants";

// The site's pagination styles.
export const pagination = tv({
  base: "mx-auto flex w-full justify-center",
});

export const paginationContent = tv({
  base: "flex flex-wrap items-center justify-center gap-0 sm:gap-1",
});

export const paginationEllipsis = tv({
  base: "flex h-9 w-4 items-center justify-center max-[359px]:hidden sm:size-10 [&_svg:not([class*='size-'])]:size-4",
});
