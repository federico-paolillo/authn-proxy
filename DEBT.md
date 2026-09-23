# Technical debt

> This record documents conscious decisions taken that might resemble shortcuts,
> workaround and shortcomings. These decisions are made to ensure the project
> remains maintanable without attempting to handle every conceivable edge case
> and enterprise-grade hardening.

## Records

- The list endpoints in `frontend/cms/client.ts` fetch a single page of up to
  100 items (Strapi's max page size). Sites that will exceed 100 published items
  in a collection must add pagination to the list pipeline. Recalibration
  trigger: any collection approaching 100 published entries.
